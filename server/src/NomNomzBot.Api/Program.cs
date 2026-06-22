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
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using NomNomzBot.Api.Configuration;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Middleware;
using NomNomzBot.Application;
using NomNomzBot.Infrastructure;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Platform.Persistence;
using Scalar.AspNetCore;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

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
                    "logs/nomnomzbot-.log",
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
    builder
        .Services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = builder.Environment.IsDevelopment();
            options.MaximumReceiveMessageSize = 128 * 1024;
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
            options.HandshakeTimeout = TimeSpan.FromSeconds(15);
            options.StatefulReconnectBufferSize = 100_000;
        })
        .AddMessagePackProtocol();

    // Hub notifiers
    builder.Services.AddScoped<IDashboardNotifier, DashboardNotifier>();
    builder.Services.AddScoped<IWidgetNotifier, WidgetNotifier>();

    // Register event handlers declared in the API layer (e.g. ChatMessageBroadcastHandler)
    builder.Services.AddEventHandlersFromAssembly(typeof(Program).Assembly);

    // JWT Auth
    string jwtSecret =
        builder.Configuration["Jwt:Secret"] ?? "change-me-in-production-at-least-32-chars!";

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

    // Rate limiting — per-user (or per-IP for anonymous) fixed window
    // Trust X-Forwarded-* from the upstream reverse proxy so the rate limiter and request log see the real
    // client IP, not the proxy's (§6). The documented deployment model puts the API behind a single trusted
    // proxy; the proxy address is not known at build time, so the default loopback-only restriction is cleared.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
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

    // Health checks
    builder
        .Services.AddHealthChecks()
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

    WebApplication app = builder.Build();

    // Wait for infrastructure dependencies before doing anything else
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

    // Run database migrations on startup
    try
    {
        Log.Information("Running database migrations...");
        await using AsyncServiceScope migrationScope = app.Services.CreateAsyncScope();
        IDatabaseMigrator migrator =
            migrationScope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();
        await migrator.MigrateAsync(CancellationToken.None);
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Database migration failed");
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

    // ─── Public web pages (deployment-distribution.md §P5) ─────────────────────
    // The bot serves its own lightweight web/ pages: the song-request page (/sr), the OBS overlay/widget
    // browser-sources (/overlay), and the OAuth landing. Served pre-auth (public). A missing web/ dir is a
    // no-op (API-only / test hosts). The dir is resolved from config or relative to the content root.
    string webRoot =
        app.Configuration["PublicWeb:RootPath"]
        ?? Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "..", "..", "web"));
    if (Directory.Exists(webRoot))
    {
        PhysicalFileProvider webFiles = new(webRoot);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = webFiles });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = webFiles });

        // Token-in-path public page: /sr/{token} resolves to the song-request shell; its JS reads the
        // token from the URL and calls the public API. Static file hits (/sr/app.js, …) are served above.
        string srShell = Path.Combine(webRoot, "sr", "index.html");
        app.MapGet("/sr/{token}", () => Results.File(srShell, "text/html"))
            .ExcludeFromDescription()
            .AllowAnonymous();

        string overlayShell = Path.Combine(webRoot, "overlay", "index.html");
        app.MapGet("/overlay/{token}", () => Results.File(overlayShell, "text/html"))
            .ExcludeFromDescription()
            .AllowAnonymous();
    }

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
    app.UseCors();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseMiddleware<TenantResolutionMiddleware>();

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

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
