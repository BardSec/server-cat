using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using ServerCat.Core.Interfaces;

// SNMP requires: Lextm.SharpSnmpLib NuGet package
// Install: dotnet add package Lextm.SharpSnmpLib

namespace ServerCat.Infrastructure.Collectors;

/// <summary>
/// STUB SNMP collector — returns a NotImplemented partial result.
///
/// IMPLEMENTATION NOTES (Phase 1.5):
///
/// SNMPv2 requires:
///   - Community string (read-only, non-default) stored in vault
///   - UDP port 161 open from app server to target
///
/// SNMPv3 requires (authPriv mode recommended):
///   - USM username, auth protocol (SHA-256), auth password
///   - Privacy protocol (AES-128), privacy password
///   - Context name (optional)
///   All stored in vault as a structured JSON secret.
///
/// OIDs to collect:
///   1.3.6.1.2.1.1.1.0  sysDescr       — OS description
///   1.3.6.1.2.1.1.5.0  sysName        — Hostname
///   1.3.6.1.2.1.1.3.0  sysUpTime      — Uptime in timeticks (1/100 sec)
///   1.3.6.1.2.1.25.2.3.1  hrStorage   — Storage table (disk info)
///   1.3.6.1.2.1.25.3.3.1  hrProcessor — CPU load
///   1.3.6.1.2.1.2.2.1     ifTable     — Network interfaces
///   1.3.6.1.2.1.25.6.3.1  hrSWInstalled — Installed software (expensive walk)
///
/// DANGER ZONE:
/// MIB support varies enormously between vendors and OS versions.
/// Test every OID against your specific devices before deploying in production.
/// SNMP walks can be slow on devices with large tables (software, interfaces).
/// </summary>
public class SnmpCollector : ICollector
{
    private readonly ILogger<SnmpCollector> _logger;

    public string CollectorType => "snmp";

    public SnmpCollector(ILogger<SnmpCollector> logger)
    {
        _logger = logger;
    }

    public Task<CollectorResult> CollectAsync(CollectorTarget target, CancellationToken ct = default)
    {
        _logger.LogWarning("SNMP collector is a stub in v1. Returning partial result for {Hostname}.", target.Hostname);

        // TODO (v1.5): Implement real SNMP collection using SharpSnmpLib.
        // Steps:
        //   1. Determine SNMP version from credential.AuthMethod
        //   2. For v2: use credential.SnmpCommunity as community string
        //   3. For v3: build OctetString auth/priv keys from credential.SnmpAuthPassword/SnmpPrivPassword
        //   4. GET sysDescr, sysName, sysUpTime
        //   5. WALK hrStorage for disk info
        //   6. WALK ifTable for network adapters
        //   7. Map OID values to CollectorResult fields

        return Task.FromResult(new CollectorResult
        {
            IsSuccess = false,
            IsPartial = true,
            ErrorMessage = "SNMP collector not yet implemented (Phase 1.5). " +
                           "Please use WinRM or WMI collector for Windows targets.",
            DurationMs = 0
        });
    }

    public async Task<ConnectivityTestResult> TestConnectivityAsync(CollectorTarget target, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var checks = new List<ConnectivityCheck>();

        // DNS check
        try
        {
            var entry = await System.Net.Dns.GetHostEntryAsync(target.Hostname, ct);
            checks.Add(new ConnectivityCheck("DNS Resolution", true, $"Resolved to {entry.AddressList.FirstOrDefault()}"));
        }
        catch (Exception ex)
        {
            checks.Add(new ConnectivityCheck("DNS Resolution", false, ex.Message));
            return new ConnectivityTestResult { Success = false, Checks = checks, DurationMs = (int)sw.ElapsedMilliseconds };
        }

        // UDP port 161 reachability (best-effort — UDP is connectionless)
        // Real test would require sending an SNMP GET and checking the response.
        // This is a TCP-based port check which doesn't truly validate SNMP.
        checks.Add(new ConnectivityCheck("SNMP Port 161/UDP", false,
            "SNMP connectivity test requires a real SNMP GET (Phase 1.5 implementation). " +
            "Verify manually that UDP 161 is accessible from this server."));

        sw.Stop();
        return new ConnectivityTestResult
        {
            Success = false,
            Checks = checks,
            Error = "SNMP connectivity test not fully implemented in v1",
            DurationMs = (int)sw.ElapsedMilliseconds
        };
    }
}
