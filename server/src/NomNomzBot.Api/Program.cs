// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using NomNomzBot.Api.Configuration;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Middleware;
using NomNomzBot.Application;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Infrastructure;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Platform.Deployment;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Platform.Security;
using Scalar.AspNetCore;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    // ── Standalone CLI: legacy backfill ──────────────────────────────────────────────────────
    // `--run-legacy-import <channelId>` runs the owner-gated legacy import in a minimal headless host with NO
    // Kestrel and NO background/hosted services, so the import owns the SQLite file exclusively (the live API must
    // be stopped). This is intercepted BEFORE the web host is built so none of the boot pipeline below runs.
    if (NomNomzBot.Api.Cli.LegacyImportCli.Matches(args))
    {
        int exitCode = await NomNomzBot.Api.Cli.LegacyImportCli.RunAsync(args);
        Log.CloseAndFlush();
        Environment.Exit(exitCode);
    }

    // Self-contained single-exe robustness: the host's default ContentRoot is the current working directory, so
    // appsettings.json (which carries the listen Urls) is only found when the exe is launched from its own folder.
    // A double-click does that, but a shortcut, a Task Scheduler entry, or any other cwd would silently fall back to
    // config defaults — and with no Urls, Kestrel binds its hardcoded port 5000, which is reserved on many Windows
    // machines (Hyper-V/WSL/Docker excluded ranges) and crashes the bind (WSAEACCES 10013). Anchor the ContentRoot to
    // the binary's own directory whenever the current directory lacks appsettings.json but the binary's directory has
    // it, so config loads from any launch location. Dev (`dotnet run`, cwd = project) and a same-folder launch are
    // unaffected (cwd already has appsettings.json → default resolution).
    string binaryDirectory = AppContext.BaseDirectory;
    bool currentDirectoryHasSettings = File.Exists(
        Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json")
    );
    bool binaryDirectoryHasSettings = File.Exists(
        Path.Combine(binaryDirectory, "appsettings.json")
    );
    WebApplicationBuilder builder = WebApplication.CreateBuilder(
        new WebApplicationOptions
        {
            Args = args,
            ContentRootPath =
                !currentDirectoryHasSettings && binaryDirectoryHasSettings ? binaryDirectory : null,
        }
    );

    // Fail fast on a broken DI graph: validate that every registered service can be
    // constructed (ValidateOnBuild) and that no singleton captures a scoped dependency
    // (ValidateScopes) — the reliability guard behind the §4 auto-discovery scan.
    builder.Host.UseDefaultServiceProvider(
        (_, options) =>
        {
            options.ValidateOnBuild = true;
            options.ValidateScopes = true;
        }
    );

    // Serilog
    builder.Host.UseSerilog(
        (ctx, lc) =>
            lc
                .ReadFrom.Configuration(ctx.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithEnvironmentName()
                .WriteTo.Console()
                // Cap rolling logs at 30 days so they do not grow without bound (§11). Paths/IDs in logs
                // are covered by the retention note in the operational runbook.
                .WriteTo.File(
                    Path.Combine(SelfHostDataPaths.LogsDirectory, "nomnomzbot-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30
                )
    );

    // Application + Infrastructure DI
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    // Controllers
    builder
        .Services.AddControllers()
        .AddJsonOptions(o =>
        {
            o.JsonSerializerOptions.PropertyNamingPolicy = System
                .Text
                .Json
                .JsonNamingPolicy
                .CamelCase;
            o.JsonSerializerOptions.DefaultIgnoreCondition = System
                .Text
                .Json
                .Serialization
                .JsonIgnoreCondition
                .WhenWritingNull;
        });

    // API Versioning
    builder
        .Services.AddApiVersioning(options =>
        {
            options.DefaultApiVersion = new(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;
            options.ReportApiVersions = true;
        })
        .AddMvc();

    // SignalR
    builder.Services.AddSignalR(options =>
    {
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();
        options.MaximumReceiveMessageSize = 128 * 1024;
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
        options.HandshakeTimeout = TimeSpan.FromSeconds(15);
        options.StatefulReconnectBufferSize = 100_000;
    });

    // Hub notifiers
    builder.Services.AddScoped<IDashboardNotifier, DashboardNotifier>();
    builder.Services.AddScoped<IWidgetNotifier, WidgetNotifier>();
    builder.Services.AddScoped<
        NomNomzBot.Application.Sound.Services.ISoundClipOverlayNotifier,
        NomNomzBot.Api.Hubs.SoundClipOverlayNotifierAdapter
    >();

    // Register event handlers declared in the API layer (e.g. ChatMessageBroadcastHandler)
    builder.Services.AddEventHandlersFromAssembly(typeof(Program).Assembly);

    // JWT Auth. The self-host single executable must run on a clean first launch — the operator never edits a
    // config file — so when no strong Jwt:Secret was supplied we generate one and persist it OS-natively
    // (SelfHostSecretStore: DPAPI / user-only file) so tokens survive restarts. A strong configured value always
    // wins; SaaS must supply its own (left as-is so the guard below rejects a weak one).
    string? configuredJwtSecret = builder.Configuration["Jwt:Secret"];
    bool isSaas = string.Equals(
        builder.Configuration["Deployment:Mode"]?.Replace("_", string.Empty),
        "saas",
        StringComparison.OrdinalIgnoreCase
    );
    string jwtSecret =
        !isSaas && StartupSecretGuard.IsWeakOrDefaultJwtSecret(configuredJwtSecret)
            ? SelfHostSecretStore.LoadOrCreateJwtSecret()
            : configuredJwtSecret ?? "change-me-in-production-at-least-32-chars!";
    builder.Configuration["Jwt:Secret"] = jwtSecret;

    // Fail fast in production rather than silently run with publicly-known default secrets (§2/§3).
    StartupSecretGuard.Validate(
        jwtSecret,
        builder.Configuration["Encryption:Key"],
        builder.Environment.IsDevelopment()
    );

    // Host-header filtering: when AllowedHosts is still the permissive "*", derive it from App:BaseUrl's host
    // (plus loopback for container health checks) so filtering is correct for any deployment from the single
    // domain the operator already configures (§9). An explicit AllowedHosts value still wins.
    if (
        builder.Configuration["AllowedHosts"] is null or "*"
        && Uri.TryCreate(builder.Configuration["App:BaseUrl"], UriKind.Absolute, out Uri? baseUri)
    )
    {
        builder.Configuration["AllowedHosts"] = $"{baseUri.Host};localhost;127.0.0.1";
    }

    builder
        .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new()
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "nomnomzbot",
                ValidAudience = builder.Configuration["Jwt:Audience"] ?? "nomnomzbot",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            };

            // Allow JWT from SignalR query string
            options.Events = new()
            {
                OnMessageReceived = ctx =>
                {
                    StringValues accessToken = ctx.Request.Query["access_token"];
                    PathString path = ctx.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                        ctx.Token = accessToken;
                    return Task.CompletedTask;
                },
            };
        });

    builder.Services.AddAuthorization();

    // Roles-permissions Gate 2 (§6): a dynamic policy provider that synthesizes a policy for any
    // rbac:<actionKey> name + the handler that enforces it via IActionAuthorizationService.
    builder.Services.AddSingleton<
        Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider,
        NomNomzBot.Api.Authorization.ActionAuthorizationPolicyProvider
    >();
    builder.Services.AddScoped<
        Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
        NomNomzBot.Api.Authorization.ActionAuthorizationHandler
    >();

    // Rate limiting — per-user (or per-IP for anonymous) sliding window.
    //
    // Trust X-Forwarded-* ONLY from explicitly-configured proxies, so the rate limiter and request log see the
    // real client IP without letting a direct caller forge it. Clearing the known-proxy lists outright (as we
    // once did) makes ASP.NET honour X-Forwarded-For from ANY source — a client could then rotate the header to
    // bypass the per-IP rate limits and spoof the client IP in audit logs (§6). The default is loopback-only,
    // which is correct for a reverse proxy terminating on the same host; set ForwardedHeaders:KnownProxies (IPs)
    // and/or :KnownNetworks (CIDRs) when the proxy reaches the API from another address — e.g. a containerised
    // cloudflared/nginx sidecar on the docker bridge network.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        // Trust X-Forwarded-Host too (in addition to For + Proto) so Request.Host reflects the public domain a
        // reverse proxy / Cloudflare tunnel fronts the bot with — every OAuth redirect_uri (Twitch, Discord,
        // Spotify, YouTube, bot) and the credential-card "register this URL" copy are built from that host, so the
        // owner registers and the bot sends the exact same URL.
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor
            | ForwardedHeaders.XForwardedProto
            | ForwardedHeaders.XForwardedHost;
        options.ForwardLimit =
            builder.Configuration.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 1;

        // Blank entries (e.g. an unset docker env var that expands to "") are dropped so they cannot wipe the
        // safe loopback default by making the trust list non-empty.
        string[] knownProxies = (
            builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? []
        )
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        string[] knownNetworks = (
            builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? []
        )
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        if (knownProxies.Length > 0 || knownNetworks.Length > 0)
        {
            // An explicit trust list replaces the framework defaults entirely.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();

            foreach (string proxy in knownProxies)
                if (System.Net.IPAddress.TryParse(proxy, out System.Net.IPAddress? ip))
                    options.KnownProxies.Add(ip);

            foreach (string network in knownNetworks)
                if (System.Net.IPNetwork.TryParse(network, out System.Net.IPNetwork parsed))
                    options.KnownIPNetworks.Add(parsed);
        }
        // Otherwise keep the framework default (loopback only): a direct caller cannot spoof X-Forwarded-For.
    });

    builder.Services.AddRateLimiter(options =>
    {
        // General API: 120 req/min per authenticated user or IP. Sliding window (6 segments) so a window
        // reset cannot be exploited to burst 2x the limit at the boundary.
        options.AddPolicy(
            "api",
            context =>
            {
                string key =
                    context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? context.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";
                return RateLimitPartition.GetSlidingWindowLimiter(
                    key,
                    _ =>
                        new()
                        {
                            PermitLimit = 120,
                            Window = TimeSpan.FromMinutes(1),
                            SegmentsPerWindow = 6,
                            QueueLimit = 0,
                        }
                );
            }
        );

        // Auth endpoints: 10 req/min per IP (brute-force protection), sliding window for the same reason.
        options.AddPolicy(
            "auth",
            context =>
            {
                string ip = context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
                return RateLimitPartition.GetSlidingWindowLimiter(
                    $"auth:{ip}",
                    _ =>
                        new()
                        {
                            PermitLimit = 10,
                            Window = TimeSpan.FromMinutes(1),
                            SegmentsPerWindow = 6,
                            QueueLimit = 0,
                        }
                );
            }
        );

        // Device-login polling: the Device Code Flow legitimately polls every ~5s (≈12 req/min) until the
        // operator approves, for up to the code's lifetime — far above the brute-force "auth" budget, so it
        // gets its own generous per-IP allowance. 60 req/min (1/s) still bounds a flood (real polling never
        // exceeds it, even with a concurrent streamer + bot login) while never throttling a legitimate login.
        // The backend's own DeviceCodePollThrottle separately caps how often each code reaches Twitch.
        options.AddPolicy(
            "device-poll",
            context =>
            {
                string ip = context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
                return RateLimitPartition.GetSlidingWindowLimiter(
                    $"device-poll:{ip}",
                    _ =>
                        new()
                        {
                            PermitLimit = 60,
                            Window = TimeSpan.FromMinutes(1),
                            SegmentsPerWindow = 6,
                            QueueLimit = 0,
                        }
                );
            }
        );

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    });

    // OpenAPI
    builder.Services.AddOpenApi();

    // CORS
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            policy
                .WithOrigins(
                    builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                        ??
                        [
                            "http://localhost:3000",
                            "http://localhost:5173",
                            "http://localhost:8081",
                            "https://bot-dev.nomercy.tv",
                        ]
                )
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        });
    });

    // Health checks — profile-selected (platform-conventions §7). The DB readiness probe is the resolved
    // provider's: AddDbContextCheck<AppDbContext> on SQLite (lite — no Npgsql probe, no Postgres dependency),
    // the Npgsql probe on Postgres (full/SaaS). The Redis check is present only when the cache provider is Redis.
    (DeploymentMode bootMode, bool _) = DeploymentModeResolver.Resolve(builder.Configuration);
    bool bootUsesDurableTier =
        DeploymentModeResolver.DbProviderFor(bootMode) == DbProviderKind.Postgres;

    Microsoft.Extensions.DependencyInjection.IHealthChecksBuilder healthChecks =
        builder.Services.AddHealthChecks();

    if (bootUsesDurableTier)
    {
        healthChecks
            .AddNpgSql(
                builder.Configuration.GetConnectionString("DefaultConnection")
                    ?? "Host=localhost;Database=nomnomzbot;Username=postgres;Password=postgres",
                name: "postgresql",
                tags: ["db", "ready"]
            )
            .AddCheck(
                "redis",
                () =>
                {
                    string? redisCs =
                        builder.Configuration.GetConnectionString("Redis")
                        ?? builder.Configuration["Redis:ConnectionString"];
                    if (string.IsNullOrWhiteSpace(redisCs))
                        return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(
                            "Redis not configured — using in-memory cache"
                        );
                    try
                    {
                        using StackExchange.Redis.ConnectionMultiplexer conn =
                            StackExchange.Redis.ConnectionMultiplexer.Connect(
                                redisCs + ",connectTimeout=2000,syncTimeout=2000"
                            );
                        conn.GetDatabase().Ping();
                        return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy();
                    }
                    catch (Exception ex)
                    {
                        return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy(
                            ex.Message
                        );
                    }
                },
                tags: ["cache", "ready"]
            );
    }
    else
    {
        // Lite: the readiness DB probe is a SQLite reachability check via the bound AppDbContext, and the
        // cache/bus are in-process (always healthy — nothing external to reach).
        healthChecks
            .AddDbContextCheck<NomNomzBot.Infrastructure.Platform.Persistence.AppDbContext>(
                name: "sqlite",
                tags: ["db", "ready"]
            )
            .AddCheck(
                "cache",
                () =>
                    Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(
                        "In-process cache/bus (no external dependency)"
                    ),
                tags: ["cache", "ready"]
            );
    }

    // ── Smart self-host port handling (deployment-distribution §6) ───────────────────────────
    // Before the host binds, resolve the actual listen port so a port conflict never crashes the bot: prefer the
    // configured port; if a stale/duplicate NomNomzBot holds it, replace that (one canonical bot); if another app
    // holds it, step aside onto a free ephemeral port (the UI discovers the real port over the LAN via mDNS). SaaS
    // is left untouched (it binds behind a proxy on a fixed port). The bootstrap logger is used because this runs
    // before the DI logger exists; the resolved port is published to IListenEndpointAccessor after Build() so the
    // self-host mDNS advertiser announces the real port.
    Microsoft.Extensions.Logging.ILogger listenPortLogger =
        new Serilog.Extensions.Logging.SerilogLoggerFactory(Log.Logger).CreateLogger("ListenPort");
    int? resolvedListenPort = ListenPortBootstrap.ResolveAndApply(
        builder.Configuration,
        bootMode,
        listenPortLogger
    );

    // ── Windows system-tray icon (self-host single binary) ───────────────────────────────────
    // The self-host binary is windowless (WinExe — no console, no main window), so a double-click leaves the
    // operator with no visible sign it is running and no obvious way to open the dashboard or stop it. Register a
    // tray icon ONLY when the gate passes: a self-host profile AND Windows AND an interactive desktop session
    // (off for SaaS, Docker/headless, a Windows Service, CI, and any redirected/automation launch). On a non-Windows
    // self-host it is simply not registered — no tray, no no-op shim. It reads the bound port from
    // IListenEndpointAccessor (published below after Build) for its dashboard URL + tooltip.
    if (SystemTrayGate.ShouldShowTray(bootMode))
        builder.Services.AddHostedService<SystemTrayHostedService>();

    WebApplication app = builder.Build();

    // Publish the bound port so the self-host mDNS advertiser advertises the actual port (deployment-distribution §6).
    if (resolvedListenPort is { } boundPort)
        app.Services.GetRequiredService<IListenEndpointAccessor>().SetPort(boundPort);

    // ── Boot pipeline (deployment-distribution §2) ───────────────────────────────────────────
    // The deployment mode was already resolved at registration time (bootMode) and every provider-specific
    // adapter bound from it. The boot order: wait for the durable tier (full/SaaS only) → migrate the resolved
    // provider's set under IRunOnceGuard → persist the DeploymentProfile row + emit the resolved event (after
    // migration, so its table exists) → seed → serve.
    bool usesDurableTier = bootUsesDurableTier;

    // On full/SaaS, wait for the durable data tier. On lite there is NO Postgres and NO Redis — skip entirely.
    if (usesDurableTier)
    {
        try
        {
            Log.Information("Waiting for PostgreSQL and Redis to be ready...");
            await using AsyncServiceScope readinessScope = app.Services.CreateAsyncScope();
            StartupReadinessChecker checker =
                readinessScope.ServiceProvider.GetRequiredService<StartupReadinessChecker>();
            await checker.WaitForPostgresAsync();
            await checker.WaitForRedisAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(
                ex,
                "Infrastructure dependency not available. "
                    + "Run 'docker-compose up -d' or configure connection strings in your .env file."
            );
            throw;
        }
    }
    else
    {
        Log.Information(
            "Deployment mode is {Mode} (SQLite + in-process cache/bus) — running zero-dependency, no Postgres/Redis wait.",
            bootMode
        );
    }

    // Migrate, once, against the resolved provider's migration set (SQLite on lite, Postgres on full/SaaS).
    // Guarded by IRunOnceGuard: a no-op on self-host (single process), a pg advisory lock on SaaS so exactly one
    // replica migrates while the others wait. The DbContext was bound to the right provider + migration assembly
    // at registration time, so MigrateAsync resolves the correct set.
    try
    {
        Log.Information(
            "Running database migrations ({Provider})...",
            DeploymentModeResolver.DbProviderFor(bootMode)
        );
        await using AsyncServiceScope migrationScope = app.Services.CreateAsyncScope();
        IRunOnceGuard runOnceGuard =
            migrationScope.ServiceProvider.GetRequiredService<IRunOnceGuard>();
        await using IAsyncDisposable? lease = await runOnceGuard.TryAcquireAsync(
            "db:migrate",
            TimeSpan.FromMinutes(5)
        );
        if (lease is not null)
        {
            IDatabaseMigrator migrator =
                migrationScope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();
            await migrator.MigrateAsync(CancellationToken.None);
        }
        else
        {
            Log.Information("Another instance is migrating; waiting for the migrated schema.");
        }
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Database migration failed");
        throw;
    }

    // Persist the single-row DeploymentProfile (P.12), probe host capabilities, set the runtime Current accessor,
    // and emit DeploymentProfileResolvedEvent — AFTER migration, so the DeploymentProfiles table (and the event
    // journal) exist. The mode this records is re-resolved here and must match the registration-time bootMode.
    try
    {
        await using AsyncServiceScope profileScope = app.Services.CreateAsyncScope();
        IDeploymentProfileService profileService =
            profileScope.ServiceProvider.GetRequiredService<IDeploymentProfileService>();
        Result<DeploymentProfileSnapshot> resolved = await profileService.DetectAndPersistAsync();
        if (resolved.IsFailure)
            throw new InvalidOperationException(
                $"Deployment profile persistence failed: {resolved.ErrorMessage}"
            );
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Deployment profile persistence failed");
        throw;
    }

    // Seed content — all ISeeder content packs, ordered, in one transaction (idempotent)
    try
    {
        Log.Information("Seeding content...");
        await using AsyncServiceScope seedScope = app.Services.CreateAsyncScope();
        SeedRunner seedRunner = seedScope.ServiceProvider.GetRequiredService<SeedRunner>();
        await seedRunner.SeedAsync(CancellationToken.None);
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Content seeding failed");
        throw;
    }

    // Middleware pipeline
    // Honour X-Forwarded-* from the trusted proxy first, so RemoteIpAddress and scheme are correct for the
    // rate limiter, request logging, and absolute-URL building downstream (§6).
    app.UseForwardedHeaders();
    app.UseMiddleware<GlobalExceptionMiddleware>();
    app.UseMiddleware<RequestLoggingMiddleware>();

    // Baseline security response headers on every response, static pages included (§9). CSP is intentionally
    // left to the page layer once a client CSP model is finalized; these four are safe defaults for an API.
    app.Use(
        async (ctx, next) =>
        {
            IHeaderDictionary headers = ctx.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
            await next();
        }
    );

    // Public-facing pages (overlays, song-request) are delivered by the widget system — compiled bundles served
    // by the bot and CDN-cached for SaaS (widgets-overlays.md), not the old static web/ folder, which is removed.
    // The realtime channel to those surfaces is the OverlayHub (mapped below).

    // OpenAPI spec + Scalar UI — exposed in development, or in production only when an operator opts in
    // (Api:ExposeDocs=true). Off by default in production so the full request/response schema is not public
    // reconnaissance for an attacker (§9).
    if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Api:ExposeDocs"))
    {
        app.MapOpenApi();
        app.MapScalarApiReference(options =>
        {
            options.Title = "NomNomzBot API";
            options.Theme = ScalarTheme.DeepSpace;
        });
    }

    if (!app.Environment.IsProduction())
    {
        app.UseHttpsRedirection();
    }
    // Serve the compiled Wasm dashboard — the SAME app the desktop client runs — from the web root, so onboarding
    // is identical in a browser at / (deployment-distribution §5). Single-origin: the served app talks to the
    // origin (this bot) that served it. Static assets are public and unauthenticated (before auth + rate limiting);
    // the API / hub / health routes below still match first, and anything unmatched falls through to the SPA entry
    // (index.html) via MapFallbackToFile at the end. With no dashboard bundled (empty web root) this is a no-op and
    // the API-only behavior is unchanged. The explicit ".wasm" mapping guarantees the correct MIME so the browser
    // instantiates the module.
    FileExtensionContentTypeProvider staticContentTypes = new();
    staticContentTypes.Mappings[".wasm"] = "application/wasm";
    app.UseStaticFiles(
        new StaticFileOptions
        {
            ContentTypeProvider = staticContentTypes,
            // The Compose resource bundle ships files with no registered MIME type (e.g. the .cvr string tables the
            // dashboard's i18n loads). Static files refuses unknown extensions by default, which 404s them and breaks
            // the app's resource loading; serve them as octet-stream instead. Safe because the web root holds only
            // the public dashboard bundle.
            ServeUnknownFileTypes = true,
            DefaultContentType = "application/octet-stream",
        }
    );

    app.UseCors();
    app.UseRateLimiter();
    app.UseAuthentication();
    // Tenant resolution MUST sit between authentication and authorization: it needs the authenticated
    // principal (to resolve the caller's own channel / verify channel access), and the [RequireAction]
    // Gate-2 policies in UseAuthorization read the resolved tenant. Placing it after UseAuthorization
    // left every action check tenant-less → an unconditional 403 on channel-scoped endpoints.
    app.UseMiddleware<TenantResolutionMiddleware>();
    app.UseAuthorization();

    app.MapControllers();

    // SignalR hubs
    app.MapHub<DashboardHub>("/hubs/dashboard");
    app.MapHub<OverlayHub>("/hubs/overlay");
    app.MapHub<OBSRelayHub>("/hubs/obs");
    app.MapHub<AdminHub>("/hubs/admin");

    // Health check — returns JSON with per-check status
    app.MapHealthChecks(
        "/health",
        new()
        {
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(
                    new
                    {
                        status = report.Status.ToString().ToLowerInvariant(),
                        checks = report.Entries.Select(e => new
                        {
                            name = e.Key,
                            status = e.Value.Status.ToString().ToLowerInvariant(),
                            description = e.Value.Description,
                            durationMs = (int)e.Value.Duration.TotalMilliseconds,
                            tags = e.Value.Tags,
                        }),
                        totalDurationMs = (int)report.TotalDuration.TotalMilliseconds,
                    }
                );
            },
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
        }
    );

    // Liveness probe (no dependency checks — just proves the process is alive)
    app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }))
        .ExcludeFromDescription();

    // Running build version, so an operator can verify what is deployed and whether a security release
    // has been applied (§15). Informational version (semver/git) when stamped, else the assembly version.
    app.MapGet(
            "/health/version",
            () =>
            {
                System.Reflection.Assembly asm = typeof(Program).Assembly;
                string version =
                    asm.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? asm.GetName().Version?.ToString()
                    ?? "unknown";
                return Results.Ok(new { version });
            }
        )
        .ExcludeFromDescription()
        .AllowAnonymous();

    // Readiness probe — checks DB + Redis connectivity before declaring ready
    app.MapHealthChecks(
            "/health/ready",
            new()
            {
                Predicate = check => check.Tags.Contains("ready"),
                ResponseWriter = async (context, report) =>
                {
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(
                        new
                        {
                            status = report.Status.ToString().ToLowerInvariant(),
                            checks = report.Entries.Select(e => new
                            {
                                name = e.Key,
                                status = e.Value.Status.ToString().ToLowerInvariant(),
                                description = e.Value.Description,
                                durationMs = (int)e.Value.Duration.TotalMilliseconds,
                            }),
                            totalDurationMs = (int)report.TotalDuration.TotalMilliseconds,
                        }
                    );
                },
                ResultStatusCodes =
                {
                    [HealthStatus.Healthy] = StatusCodes.Status200OK,
                    [HealthStatus.Degraded] = StatusCodes.Status200OK,
                    [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
                },
            }
        )
        .ExcludeFromDescription();

    // Suppress browser-generated favicon requests from producing 500 errors
    app.MapGet("/favicon.ico", () => Results.NotFound()).ExcludeFromDescription();

    // SPA fallback: any route not matched by an API / hub / health endpoint or a static file serves the dashboard's
    // entry document, so a browser hitting / (or a client-side deep link) loads the Compose/Wasm app shell. Returns
    // 404 when no dashboard is bundled (empty web root), preserving the API-only behavior.
    app.MapFallbackToFile("index.html");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");

    // The self-host single binary is windowless (WinExe) — there is no console to read or hold open — so a fatal
    // startup error is surfaced in a dialog instead of vanishing. No-op for services / Docker / redirected runs.
    StartupErrorNotifier.Notify(ex);
}
finally
{
    Log.CloseAndFlush();
}
