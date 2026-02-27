using System.Runtime.InteropServices;
using System.Text;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using ServerCat.Api.Middleware;
using ServerCat.Core.Interfaces;
using ServerCat.Infrastructure.Collectors;
using ServerCat.Infrastructure.Data;
using ServerCat.Infrastructure.Security;
using ServerCat.Infrastructure.Services;

// ── Bootstrap Serilog before anything else ────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog ───────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, services, config) => config
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"));

    // ── Windows Service hosting ───────────────────────────────────────────────
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        builder.Host.UseWindowsService(options => options.ServiceName = "ServerCat");

    // ── Database ──────────────────────────────────────────────────────────────
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
            npg => npg.EnableRetryOnFailure(3)));

    // ── Vault / credential store ──────────────────────────────────────────────
    builder.Services.Configure<VaultOptions>(
        builder.Configuration.GetSection(VaultOptions.SectionName));

    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        builder.Services.AddSingleton<ICredentialStore, DpapiCredentialStore>();
    else
    {
        // Non-Windows fallback: file-based AES store (development only)
        // ASSUMPTION: DpapiCredentialStore will not run on Linux.
        // Replace with HashiCorp Vault or similar for production Linux deployments.
        Log.Warning("Running on non-Windows platform. DPAPI credential store unavailable. " +
                    "Using stub credential store — DO NOT use in production.");
        builder.Services.AddSingleton<ICredentialStore, StubCredentialStore>();
    }

    // ── Collectors ────────────────────────────────────────────────────────────
    builder.Services.Configure<WinRmOptions>(
        builder.Configuration.GetSection(WinRmOptions.SectionName));
    builder.Services.AddSingleton(sp =>
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WinRmOptions>>().Value);

    builder.Services.AddScoped<ICollector, WinRmCollector>();
    builder.Services.AddScoped<ICollector, SnmpCollector>();
    builder.Services.AddScoped<CollectorFactory>();
    builder.Services.AddScoped<IPollingService, PollingService>();

    // ── Hangfire ──────────────────────────────────────────────────────────────
    var hangfireConnStr = builder.Configuration.GetConnectionString("DefaultConnection");
    builder.Services.AddHangfire(config => config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(c => c.UseNpgsqlConnection(hangfireConnStr)));

    builder.Services.AddHangfireServer(options =>
    {
        options.WorkerCount = builder.Configuration.GetValue<int>("Hangfire:MaxConcurrentJobs", 10);
        options.Queues = new[] { "default", "maintenance" };
    });

    // ── Authentication / Authorization ────────────────────────────────────────
    var authMode = builder.Configuration["Authentication:Mode"] ?? "JWT";

    if (authMode.Equals("Windows", StringComparison.OrdinalIgnoreCase) &&
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        // Windows Authentication (IIS-hosted)
        builder.Services.AddAuthentication(Microsoft.AspNetCore.Server.IISIntegration.IISDefaults.AuthenticationScheme);
    }
    else
    {
        // JWT Bearer (default for self-hosted / dev)
        var jwtSecret = builder.Configuration["Authentication:JwtSecret"]
            ?? throw new InvalidOperationException("Authentication:JwtSecret must be configured.");
        var jwtIssuer = builder.Configuration["Authentication:JwtIssuer"] ?? "https://servercat.local";

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtIssuer,
                    ValidateAudience = false,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(5)
                };
            });
    }

    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    });

    // ── API ───────────────────────────────────────────────────────────────────
    builder.Services.AddControllers()
        .AddJsonOptions(opts =>
        {
            opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            opts.JsonSerializerOptions.DefaultIgnoreCondition =
                System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        });

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo { Title = "ServerCat API", Version = "v1" });
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.ApiKey,
            Scheme = "Bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header
        });
        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
                Array.Empty<string>()
            }
        });
    });

    // ── Health checks ─────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks()
        .AddNpgSql(builder.Configuration.GetConnectionString("DefaultConnection")!,
                   name: "database");

    // ── CORS ──────────────────────────────────────────────────────────────────
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? new[] { "https://localhost:5173" };

    builder.Services.AddCors(options =>
        options.AddPolicy("AppPolicy", policy =>
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials()));

    // ── Rate limiting ─────────────────────────────────────────────────────────
    builder.Services.AddRateLimiter(options =>
        options.AddFixedWindowLimiter("api", limiter =>
        {
            limiter.Window = TimeSpan.FromMinutes(1);
            limiter.PermitLimit = 300;
        }));

    // ==========================================================================
    var app = builder.Build();
    // ==========================================================================

    // ── Auto-migrate database on startup ──────────────────────────────────────
    using (var scope = app.Services.CreateScope())
    {
        var dbCtx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbCtx.Database.MigrateAsync();
        Log.Information("Database migration completed.");
    }

    // ── Middleware pipeline ───────────────────────────────────────────────────
    app.UseMiddleware<ErrorHandlingMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseRateLimiter();
    app.UseCors("AppPolicy");

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "ServerCat API v1"));
    }

    app.UseStaticFiles(); // Serve React SPA from wwwroot

    app.UseAuthentication();
    app.UseAuthorization();

    // Hangfire dashboard — Admin role only
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new[] { new HangfireAdminAuth() }
    });

    app.MapControllers();
    app.MapHealthChecks("/health");

    // SPA fallback: serve index.html for all non-API routes
    app.MapFallbackToFile("index.html");

    // ── Schedule polls for all active servers on startup ─────────────────────
    using (var scope = app.Services.CreateScope())
    {
        var pollService = scope.ServiceProvider.GetRequiredService<IPollingService>();
        var dbCtx = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var servers = await dbCtx.Servers
            .Where(s => s.IsActive && s.PollingEnabled)
            .Select(s => new { s.Id, s.PollingIntervalMinutes })
            .ToListAsync();

        foreach (var server in servers)
            pollService.ScheduleServer(server.Id, server.PollingIntervalMinutes);

        Log.Information("Scheduled {Count} recurring poll jobs.", servers.Count);
    }

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application startup failed.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// ── Hangfire dashboard auth ───────────────────────────────────────────────────
public class HangfireAdminAuth : Hangfire.Dashboard.IDashboardAuthorizationFilter
{
    public bool Authorize(Hangfire.Dashboard.DashboardContext context)
    {
        var http = context.GetHttpContext();
        return http.User.Identity?.IsAuthenticated == true && http.User.IsInRole("Admin");
    }
}

// ── Stub credential store (non-Windows / dev only) ────────────────────────────
public class StubCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, string> _store = new();

    public Task<string> ProtectAsync(string plaintext)
    {
        var key = $"stub:{Guid.NewGuid()}";
        // WARNING: This stores plaintext in memory. For development only.
        _store[key] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext));
        return Task.FromResult(key);
    }

    public Task<string> UnprotectAsync(string storeKey)
    {
        if (!_store.TryGetValue(storeKey, out var value))
            throw new KeyNotFoundException($"Key not found: {storeKey}");
        return Task.FromResult(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value)));
    }

    public Task DeleteAsync(string storeKey) { _store.Remove(storeKey); return Task.CompletedTask; }

    public Task UpdateAsync(string storeKey, string newPlaintext)
    {
        _store[storeKey] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(newPlaintext));
        return Task.CompletedTask;
    }
}
