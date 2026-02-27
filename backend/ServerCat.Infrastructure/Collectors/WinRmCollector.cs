using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ServerCat.Core.Interfaces;

namespace ServerCat.Infrastructure.Collectors;

/// <summary>
/// Collects server inventory via WinRM / PowerShell remoting.
///
/// PREREQUISITES ON TARGET:
///   - WinRM enabled: Enable-PSRemoting -Force
///   - WinRM HTTPS listener on port 5986 (recommended)
///   - Service account is member of "Remote Management Users" group
///   - JEA constrained endpoint "ServerCatPolling" configured (recommended)
///
/// REQUIRED RIGHTS (principle of least privilege):
///   - Member of "Remote Management Users" local group on each target
///   - NOT a local administrator (JEA endpoint restricts runnable commands)
///
/// NULL SAFETY: All PowerShell property reads use null-safe extraction.
/// If a query fails, the result is marked Partial rather than Failed.
/// </summary>
public class WinRmCollector : ICollector
{
    private readonly ILogger<WinRmCollector> _logger;
    private readonly WinRmOptions _options;

    public string CollectorType => "winrm";

    public WinRmCollector(ILogger<WinRmCollector> logger, WinRmOptions options)
    {
        _logger = logger;
        _options = options;
    }

    public async Task<CollectorResult> CollectAsync(CollectorTarget target, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var errors = new List<string>();

        using var credential = target.Credential;
        var wsmanUri = BuildWsManUri(target);

        WSManConnectionInfo? connectionInfo = null;
        Runspace? runspace = null;

        try
        {
            connectionInfo = BuildConnectionInfo(wsmanUri, target.Credential);
            runspace = RunspaceFactory.CreateRunspace(connectionInfo);

            await Task.Run(() => runspace.Open(), ct);

            using var ps = PowerShell.Create();
            ps.Runspace = runspace;

            // ── 1. Host identity + OS ────────────────────────────────────
            string? hostnameResolved = null, fqdn = null, domain = null;
            string? osName = null, osVersion = null, osBuild = null;
            DateTime? installDate = null, lastBoot = null;
            long? uptimeSeconds = null;

            try
            {
                ps.Commands.Clear();
                ps.AddScript(@"
                    $os = Get-CimInstance Win32_OperatingSystem
                    $cs = Get-CimInstance Win32_ComputerSystem
                    [PSCustomObject]@{
                        HostName        = $env:COMPUTERNAME
                        Fqdn            = ([System.Net.Dns]::GetHostEntry('localhost')).HostName
                        Domain          = $cs.Domain
                        OsName          = $os.Caption
                        OsVersion       = $os.Version
                        OsBuild         = $os.BuildNumber
                        InstallDate     = $os.InstallDate
                        LastBootTime    = $os.LastBootUpTime
                        UptimeSeconds   = (New-TimeSpan -Start $os.LastBootUpTime -End (Get-Date)).TotalSeconds
                    }
                ");

                var results = await Task.Run(() => ps.Invoke(), ct);
                if (results.Count > 0)
                {
                    var r = results[0];
                    hostnameResolved = GetProp<string>(r, "HostName");
                    fqdn = GetProp<string>(r, "Fqdn");
                    domain = GetProp<string>(r, "Domain");
                    osName = GetProp<string>(r, "OsName");
                    osVersion = GetProp<string>(r, "OsVersion");
                    osBuild = GetProp<string>(r, "OsBuild");
                    installDate = GetProp<DateTime?>(r, "InstallDate");
                    lastBoot = GetProp<DateTime?>(r, "LastBootTime");
                    uptimeSeconds = (long?)GetProp<double?>(r, "UptimeSeconds");
                }
                CheckPsErrors(ps, "OS query", errors);
            }
            catch (Exception ex)
            {
                errors.Add($"OS query failed: {ex.Message}");
            }

            // ── 2. Hardware (CPU + Memory) ────────────────────────────────
            CpuInfo? cpuInfo = null;
            long? memTotalMb = null, memAvailMb = null;

            try
            {
                ps.Commands.Clear();
                ps.AddScript(@"
                    $cpu = Get-CimInstance Win32_Processor
                    $os  = Get-CimInstance Win32_OperatingSystem
                    [PSCustomObject]@{
                        CpuModel          = ($cpu | Select-Object -First 1).Name.Trim()
                        Sockets           = ($cpu | Measure-Object).Count
                        CoresPerSocket    = ($cpu | Select-Object -First 1).NumberOfCores
                        LogicalProcessors = ($cpu | Measure-Object -Property NumberOfLogicalProcessors -Sum).Sum
                        TotalMemMb        = [math]::Round($os.TotalVisibleMemorySize / 1024)
                        FreeMemMb         = [math]::Round($os.FreePhysicalMemory / 1024)
                    }
                ");

                var results = await Task.Run(() => ps.Invoke(), ct);
                if (results.Count > 0)
                {
                    var r = results[0];
                    cpuInfo = new CpuInfo(
                        Model: GetProp<string>(r, "CpuModel") ?? "Unknown",
                        Sockets: GetProp<int>(r, "Sockets"),
                        CoresPerSocket: GetProp<int>(r, "CoresPerSocket"),
                        LogicalProcessors: GetProp<int>(r, "LogicalProcessors")
                    );
                    memTotalMb = GetProp<long?>(r, "TotalMemMb");
                    memAvailMb = GetProp<long?>(r, "FreeMemMb");
                }
                CheckPsErrors(ps, "Hardware query", errors);
            }
            catch (Exception ex)
            {
                errors.Add($"Hardware query failed: {ex.Message}");
            }

            // ── 3. Disk Volumes ───────────────────────────────────────────
            var diskVolumes = new List<DiskVolume>();

            try
            {
                ps.Commands.Clear();
                ps.AddScript(@"
                    Get-CimInstance Win32_LogicalDisk -Filter ""DriveType=3"" |
                    Select-Object DeviceID, VolumeName, FileSystem,
                        @{n='SizeGb'; e={[math]::Round($_.Size/1GB,1)}},
                        @{n='FreeGb'; e={[math]::Round($_.FreeSpace/1GB,1)}}
                ");

                var results = await Task.Run(() => ps.Invoke(), ct);
                foreach (var r in results)
                {
                    var sizeGb = GetProp<double>(r, "SizeGb");
                    var freeGb = GetProp<double>(r, "FreeGb");
                    diskVolumes.Add(new DiskVolume(
                        Drive: GetProp<string>(r, "DeviceID") ?? "?",
                        Label: GetProp<string>(r, "VolumeName"),
                        Filesystem: GetProp<string>(r, "FileSystem"),
                        SizeGb: sizeGb,
                        FreeGb: freeGb
                    ));
                }
                CheckPsErrors(ps, "Disk query", errors);
            }
            catch (Exception ex)
            {
                errors.Add($"Disk query failed: {ex.Message}");
            }

            // ── 4. Network Adapters ───────────────────────────────────────
            var networkAdapters = new List<NetworkAdapter>();

            try
            {
                ps.Commands.Clear();
                ps.AddScript(@"
                    $adapters = Get-NetAdapter | Where-Object { $_.Status -eq 'Up' }
                    foreach ($a in $adapters) {
                        $ip    = Get-NetIPAddress -InterfaceIndex $a.InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue | Select-Object -First 1
                        $gw    = (Get-NetRoute -InterfaceIndex $a.InterfaceIndex -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue | Select-Object -First 1).NextHop
                        $dns   = (Get-DnsClientServerAddress -InterfaceIndex $a.InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue).ServerAddresses
                        [PSCustomObject]@{
                            Name       = $a.Name
                            IpAddress  = $ip.IPAddress
                            SubnetLen  = $ip.PrefixLength
                            MacAddress = $a.MacAddress
                            Gateway    = $gw
                            DnsServers = $dns -join ','
                        }
                    }
                ");

                var results = await Task.Run(() => ps.Invoke(), ct);
                foreach (var r in results)
                {
                    var dnsRaw = GetProp<string>(r, "DnsServers") ?? string.Empty;
                    networkAdapters.Add(new NetworkAdapter(
                        Name: GetProp<string>(r, "Name") ?? "Unknown",
                        IpAddress: GetProp<string>(r, "IpAddress"),
                        SubnetMask: GetProp<string>(r, "SubnetLen"),
                        MacAddress: GetProp<string>(r, "MacAddress"),
                        DefaultGateway: GetProp<string>(r, "Gateway"),
                        DnsServers: dnsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    ));
                }
                CheckPsErrors(ps, "Network query", errors);
            }
            catch (Exception ex)
            {
                errors.Add($"Network query failed: {ex.Message}");
            }

            // ── 5. Services ───────────────────────────────────────────────
            var services = new List<ServiceInfo>();

            try
            {
                ps.Commands.Clear();
                ps.AddScript(@"
                    Get-CimInstance Win32_Service |
                    Select-Object Name, DisplayName, State, StartMode |
                    Sort-Object Name
                ");

                var results = await Task.Run(() => ps.Invoke(), ct);
                foreach (var r in results)
                {
                    services.Add(new ServiceInfo(
                        Name: GetProp<string>(r, "Name") ?? string.Empty,
                        DisplayName: GetProp<string>(r, "DisplayName") ?? string.Empty,
                        Status: GetProp<string>(r, "State") ?? "Unknown",
                        StartType: GetProp<string>(r, "StartMode") ?? "Unknown"
                    ));
                }
                CheckPsErrors(ps, "Services query", errors);
            }
            catch (Exception ex)
            {
                errors.Add($"Services query failed: {ex.Message}");
            }

            // ── 6. Patch status ───────────────────────────────────────────
            DateOnly? lastUpdate = null;
            bool? pendingReboot = null;

            try
            {
                ps.Commands.Clear();
                ps.AddScript(@"
                    $hotfix = Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 1
                    $cbsKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing'
                    $rebootPending = Test-Path (Join-Path $cbsKey 'RebootPending')
                    [PSCustomObject]@{
                        LastUpdateDate = $hotfix.InstalledOn
                        PendingReboot  = $rebootPending
                    }
                ");

                var results = await Task.Run(() => ps.Invoke(), ct);
                if (results.Count > 0)
                {
                    var r = results[0];
                    var rawDate = GetProp<DateTime?>(r, "LastUpdateDate");
                    if (rawDate.HasValue)
                        lastUpdate = DateOnly.FromDateTime(rawDate.Value);
                    pendingReboot = GetProp<bool?>(r, "PendingReboot");
                }
                CheckPsErrors(ps, "Patch query", errors);
            }
            catch (Exception ex)
            {
                errors.Add($"Patch query failed (non-critical): {ex.Message}");
            }

            sw.Stop();

            var isPartial = errors.Count > 0;
            return new CollectorResult
            {
                IsSuccess = !isPartial,
                IsPartial = isPartial,
                ErrorMessage = isPartial ? string.Join("; ", errors) : null,
                DurationMs = (int)sw.ElapsedMilliseconds,
                HostnameResolved = hostnameResolved,
                Fqdn = fqdn,
                Domain = domain,
                OsName = osName,
                OsVersion = osVersion,
                OsBuild = osBuild,
                InstallDate = installDate,
                LastBootTime = lastBoot,
                UptimeSeconds = uptimeSeconds,
                CpuInfo = cpuInfo,
                MemoryTotalMb = memTotalMb,
                MemoryAvailableMb = memAvailMb,
                DiskVolumes = diskVolumes,
                NetworkAdapters = networkAdapters,
                Services = services,
                LastUpdateInstalled = lastUpdate,
                PendingReboot = pendingReboot
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "WinRM collection failed for {Hostname}", target.Hostname);
            return new CollectorResult
            {
                IsSuccess = false,
                IsPartial = false,
                ErrorMessage = ex.Message,
                DurationMs = (int)sw.ElapsedMilliseconds
            };
        }
        finally
        {
            runspace?.Dispose();
        }
    }

    public async Task<ConnectivityTestResult> TestConnectivityAsync(CollectorTarget target, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var checks = new List<ConnectivityCheck>();
        string? lastError = null;

        // Check 1: DNS resolution
        string resolvedIp = string.Empty;
        try
        {
            var entry = await System.Net.Dns.GetHostEntryAsync(target.Hostname, ct);
            resolvedIp = entry.AddressList.FirstOrDefault()?.ToString() ?? "?";
            checks.Add(new ConnectivityCheck("DNS Resolution", true, $"Resolved to {resolvedIp}"));
        }
        catch (Exception ex)
        {
            checks.Add(new ConnectivityCheck("DNS Resolution", false, ex.Message));
            lastError = ex.Message;
            return new ConnectivityTestResult { Success = false, Checks = checks, Error = lastError, DurationMs = (int)sw.ElapsedMilliseconds };
        }

        // Check 2: TCP port
        int port = _options.UseHttps ? 5986 : 5985;
        string host = string.IsNullOrEmpty(target.IpAddress) ? target.Hostname : target.IpAddress;
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(host, port, ct);
            checks.Add(new ConnectivityCheck($"TCP Port {port}", true, $"Connected to {host}:{port}"));
        }
        catch (Exception ex)
        {
            checks.Add(new ConnectivityCheck($"TCP Port {port}", false, ex.Message));
            lastError = ex.Message;
            return new ConnectivityTestResult { Success = false, Checks = checks, Error = lastError, DurationMs = (int)sw.ElapsedMilliseconds };
        }

        // Check 3: WinRM authentication
        try
        {
            var wsmanUri = BuildWsManUri(target);
            var connInfo = BuildConnectionInfo(wsmanUri, target.Credential);
            using var runspace = RunspaceFactory.CreateRunspace(connInfo);
            await Task.Run(() => runspace.Open(), ct);

            using var ps = PowerShell.Create();
            ps.Runspace = runspace;
            ps.AddScript("$env:COMPUTERNAME");
            var results = await Task.Run(() => ps.Invoke(), ct);

            checks.Add(new ConnectivityCheck("WinRM Authentication", true, $"Authenticated successfully"));

            // Check 4: Basic CIM query
            ps.Commands.Clear();
            ps.AddScript("(Get-CimInstance Win32_OperatingSystem).Caption");
            results = await Task.Run(() => ps.Invoke(), ct);
            var osCaption = results.FirstOrDefault()?.ToString() ?? "?";
            checks.Add(new ConnectivityCheck("CIM Query (Win32_OperatingSystem)", true, osCaption));
        }
        catch (Exception ex)
        {
            checks.Add(new ConnectivityCheck("WinRM Authentication", false, ex.Message));
            lastError = ex.Message;
        }

        sw.Stop();
        return new ConnectivityTestResult
        {
            Success = checks.All(c => c.Passed),
            Checks = checks,
            Error = lastError,
            DurationMs = (int)sw.ElapsedMilliseconds
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private Uri BuildWsManUri(CollectorTarget target)
    {
        var host = string.IsNullOrEmpty(target.IpAddress) ? target.Hostname : target.IpAddress;
        var scheme = _options.UseHttps ? "https" : "http";
        var port = _options.UseHttps ? 5986 : 5985;
        return new Uri($"{scheme}://{host}:{port}/wsman");
    }

    private WSManConnectionInfo BuildConnectionInfo(Uri wsmanUri, ResolvedCredential credential)
    {
        var psCredential = string.IsNullOrEmpty(credential.Username) ? null
            : new PSCredential(credential.Username,
                System.Security.SecureStringExtensions.ToSecureString(credential.Secret ?? string.Empty));

        var info = new WSManConnectionInfo(wsmanUri)
        {
            AuthenticationMechanism = _options.UseKerberos
                ? AuthenticationMechanism.Kerberos
                : AuthenticationMechanism.Negotiate,
            OperationTimeout = (int)_options.OperationTimeout.TotalMilliseconds,
            OpenTimeout = (int)_options.ConnectTimeout.TotalMilliseconds,
        };

        if (psCredential != null)
            info.Credential = psCredential;

        if (_options.UseHttps && _options.SkipCaCheck)
            info.SkipCACheck = true;

        if (!string.IsNullOrEmpty(_options.JeaConfigurationName))
            info.ShellUri = $"http://schemas.microsoft.com/powershell/{_options.JeaConfigurationName}";

        return info;
    }

    private static T? GetProp<T>(PSObject obj, string name)
    {
        try
        {
            var val = obj.Properties[name]?.Value;
            if (val == null) return default;
            return (T)Convert.ChangeType(val, typeof(T).IsGenericType
                ? Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T)
                : typeof(T));
        }
        catch
        {
            return default;
        }
    }

    private static void CheckPsErrors(PowerShell ps, string context, List<string> errors)
    {
        if (ps.HadErrors)
        {
            foreach (var err in ps.Streams.Error)
                errors.Add($"{context}: {err.Exception?.Message ?? err.ToString()}");
        }
    }
}

public static class SecureStringExtensions
{
    public static System.Security.SecureString ToSecureString(string value)
    {
        var ss = new System.Security.SecureString();
        foreach (var c in value) ss.AppendChar(c);
        ss.MakeReadOnly();
        return ss;
    }
}

public class WinRmOptions
{
    public const string SectionName = "WinRm";

    public bool UseHttps { get; set; } = true;
    public bool UseKerberos { get; set; } = true;

    /// <summary>
    /// WARNING: Only set to true in lab environments with self-signed certs.
    /// Never use in production unless you have a plan to migrate to trusted certs.
    /// </summary>
    public bool SkipCaCheck { get; set; } = false;

    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>
    /// JEA constrained endpoint name. Set to "ServerCatPolling" if JEA is configured.
    /// Leave null/empty to use the default PowerShell endpoint.
    /// </summary>
    public string? JeaConfigurationName { get; set; }
}
