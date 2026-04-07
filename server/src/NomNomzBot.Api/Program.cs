// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) NoMercy Entertainment. All rights reserved.

using System.Text;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using NoMercyBot.Api.Hubs;
using NoMercyBot.Api.Middleware;
using NoMercyBot.Application;
using NoMercyBot.Infrastructure;
using NoMercyBot.Infrastructure.Persistence;
using NoMercyBot.Infrastructure.Services.General;
using Scalar.AspNetCore;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    // Serilog
    builder.Host.UseSerilog(
        (ctx, lc) =>
            lc
                .ReadFrom.Configuration(ctx.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithEnvironmentName()
                .WriteTo.Console()
                .WriteTo.File("logs/nomercybot-.log", rollingInterval: RollingInterval.Day)
    );

    // Application + Infrastructure DI
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    // Controllers
    builder.Services.AddControllers()
        .AddJsonOptions(o =>
        {
            o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            o.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
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
                ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "nomercybot",
                ValidAudience = builder.Configuration["Jwt:Audience"] ?? "nomercybot",
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

    // Rate limiting — per-user (or per-IP for anonymous) fixed window
    builder.Services.AddRateLimiter(options =>
    {
        // General API: 120 req/min per authenticated user or IP
        options.AddPolicy(
            "api",
            context =>
            {
                string key =
                    context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? context.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(
                    key,
                    _ =>
                        new()
                        {
                            PermitLimit = 120,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                        }
                );
            }
        );

        // Auth endpoints: 10 req/min per IP (brute-force protection)
        options.AddPolicy(
            "auth",
            context =>
            {
                string ip = context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(
                    $"auth:{ip}",
                    _ =>
                        new()
                        {
                            PermitLimit = 10,
                            Window = TimeSpan.FromMinutes(1),
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
                        ?? [
                        "http://localhost:3000",
                        "http://localhost:5173",
                        "http://localhost:8081",
                        "https://bot-dev.nomercy.tv"
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
                ?? "Host=localhost;Database=nomercybot;Username=postgres;Password=postgres",
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

    // Seed reference data
    try
    {
        Log.Information("Seeding reference data...");
        await using AsyncServiceScope seedScope = app.Services.CreateAsyncScope();
        DataSeeder seeder = seedScope.ServiceProvider.GetRequiredService<DataSeeder>();
        await seeder.SeedAsync(CancellationToken.None);
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Data seeding failed");
        throw;
    }

    // Middleware pipeline
    app.UseMiddleware<GlobalExceptionMiddleware>();
    app.UseMiddleware<RequestLoggingMiddleware>();

    // OpenAPI spec + Scalar UI — always available (self-hosted / local dev)
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "NomNomzBot API";
        options.Theme = ScalarTheme.DeepSpace;
    });

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
    ).ExcludeFromDescription();

    // Suppress browser-generated favicon requests from producing 500 errors
    app.MapGet("/favicon.ico", () => Results.NotFound())
        .ExcludeFromDescription();

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
