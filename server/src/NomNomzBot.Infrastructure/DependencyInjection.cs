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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NomNomzBot.Application.Abstractions;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Abstractions.Eventing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.RateLimiting;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Platform.Services;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Tts.Interfaces;
using NomNomzBot.Infrastructure.BackgroundServices;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Commands.Jobs;
using NomNomzBot.Infrastructure.Commands.Persistence;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Identity.Persistence;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Persistence;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Platform.Auth;
using NomNomzBot.Infrastructure.Platform.Caching;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Platform.Persistence.Interceptors;
using NomNomzBot.Infrastructure.Platform.Persistence.Repositories;
using NomNomzBot.Infrastructure.Platform.RateLimiting;
using NomNomzBot.Infrastructure.Platform.Resilience;
using NomNomzBot.Infrastructure.Platform.Scheduling;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Platform.Transport;
using NomNomzBot.Infrastructure.Chat.PipelineActions;
using NomNomzBot.Infrastructure.Moderation.PipelineActions;
using NomNomzBot.Infrastructure.Music.PipelineActions;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.Pipeline.CoreActions;
using NomNomzBot.Infrastructure.Stream.PipelineActions;
using NomNomzBot.Infrastructure.Rewards;
using NomNomzBot.Infrastructure.Rewards.Persistence;
using NomNomzBot.Infrastructure.Tts;
using NomNomzBot.Infrastructure.Widgets;
using NomNomzBot.Infrastructure.Widgets.Persistence;

namespace NomNomzBot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        // Interceptors (scoped so they can resolve scoped services like ICurrentTenantService)
        services.AddScoped<AuditableEntityInterceptor>();
        services.AddScoped<SoftDeleteInterceptor>();
        services.AddScoped<TenantStampInterceptor>();

        // DbContext with Npgsql and interceptors
        string? connectionString =
            configuration.GetConnectionString("DefaultConnection")
            ?? configuration.GetSection("Database:ConnectionString").Value;

        services.AddDbContext<AppDbContext>(
            (serviceProvider, options) =>
            {
                options.UseNpgsql(
                    connectionString,
                    npgsqlOptions =>
                    {
                        npgsqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    }
                );

                options.AddInterceptors(
                    serviceProvider.GetRequiredService<AuditableEntityInterceptor>(),
                    serviceProvider.GetRequiredService<SoftDeleteInterceptor>(),
                    serviceProvider.GetRequiredService<TenantStampInterceptor>()
                );

                // All soft-deletable dependents (Command, Reward, Widget, etc.) and their
                // principal Channel share the same DeletedAt == null query filter, so EF's
                // PossibleIncorrectRequiredNavigationWithQueryFilterInteraction warning is a
                // false positive here. Suppress it to keep startup logs clean.
                options.ConfigureWarnings(w =>
                    w.Ignore(
                        Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId
                            .PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning
                    )
                );
            }
        );

        services.AddScoped<IApplicationDbContext>(provider =>
            provider.GetRequiredService<AppDbContext>()
        );

        // EventBus (singleton -- resolves scoped handlers internally via IServiceProvider)
        services.AddSingleton<EventLogger>();
        services.AddSingleton<NomNomzBot.Domain.Platform.Interfaces.IEventBus, EventBus>();

        // Auto-register IEventHandler<T> implementations from Infrastructure assembly
        RegisterEventHandlers(services, typeof(DependencyInjection).Assembly);

        // Security — AES-256 encryption with a stable configured key (survives container restarts)
        services.Configure<EncryptionOptions>(configuration.GetSection(EncryptionOptions.SectionName));
        services.AddSingleton<IEncryptionService, EncryptionService>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        // Caching — use Redis if configured, otherwise fall back to in-memory
        string? redisConnectionString =
            configuration.GetConnectionString("Redis") ?? configuration["Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = "nomercybot:";
            });
            services.AddSingleton<ICacheService, DistributedCacheService>();
        }
        else
        {
            services.AddMemoryCache();
            services.AddDistributedMemoryCache();
            services.AddSingleton<ICacheService, MemoryCacheService>();
        }

        // General services
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<ICooldownManager, CooldownManager>();
        services.AddSingleton<ITemplateEngine, TemplateEngine>();
        services.AddSingleton<ITemplateResolver, TemplateResolver>();
        services.AddSingleton<ITrustService, TrustService>();

        // Pipeline actions (transient — stateless)
        services.AddTransient<ICommandAction, SendMessageAction>();
        services.AddTransient<ICommandAction, SendReplyAction>();
        services.AddTransient<ICommandAction, TimeoutAction>();
        services.AddTransient<ICommandAction, BanAction>();
        services.AddTransient<ICommandAction, WaitAction>();
        services.AddTransient<ICommandAction, SetVariableAction>();
        services.AddTransient<ICommandAction, StopAction>();
        services.AddTransient<ICommandAction, DeleteMessageAction>();
        services.AddTransient<ICommandAction, ShoutoutAction>();
        services.AddTransient<ICommandAction, SongRequestAction>();
        services.AddTransient<ICommandAction, SongSkipAction>();
        services.AddTransient<ICommandAction, SongCurrentAction>();
        services.AddTransient<ICommandAction, SongQueueAction>();
        services.AddTransient<ICommandAction, SongVolumeAction>();

        // Pipeline conditions (transient — stateless)
        services.AddTransient<ICommandCondition, UserRoleCondition>();
        services.AddTransient<ICommandCondition, RandomCondition>();

        // PipelineEngine (scoped — consumes scoped services like IChatProvider, IApplicationDbContext)
        services.AddScoped<IPipelineEngine, PipelineEngine>();

        // Identity / tenant
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentTenantService, CurrentTenantService>();
        services.AddScoped<IChannelAccessService, ChannelAccessService>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // Startup readiness checker and database migrator
        services.AddTransient<StartupReadinessChecker>();
        services.AddScoped<IDatabaseMigrator, DatabaseMigrator>();
        services.AddScoped<DataSeeder>();

        // Repositories
        services.AddScoped<ChannelRepository>();
        services.AddScoped<CommandRepository>();
        services.AddScoped<RewardRepository>();
        services.AddScoped<UserRepository>();
        services.AddScoped<WidgetRepository>();

        // UnitOfWork
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Application services
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<ICommandService, CommandService>();
        services.AddScoped<IChannelService, ChannelService>();
        services.AddScoped<IRewardService, RewardService>();
        services.AddScoped<IWidgetService, WidgetService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IModerationService, ModerationService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<ITimerManagementService, TimerManagementService>();
        services.AddScoped<IMusicConfigService, MusicConfigService>();
        services.AddScoped<ITtsConfigService, TtsConfigService>();
        services.AddScoped<IEventResponseService, EventResponseService>();
        services.AddScoped<IPipelineService, PipelineService>();
        services.AddScoped<IFeatureService, FeatureService>();

        // GDPR + migration (scoped — use DbContext)
        services.AddScoped<IGdprService, GdprService>();
        services.AddScoped<SqliteMigrationService>();

        // Auto-moderation (scoped — uses ICooldownManager which is singleton, fine)
        services.AddScoped<AutoModerationEngine>();

        // Music providers + service (singleton — maintain per-channel queues)
        services.AddHttpClient("edge-tts");
        services.AddSingleton<ITtsProvider, EdgeTtsProvider>();
        services.AddHttpClient("azure-tts");
        services.AddSingleton<ITtsProvider>(sp => new AzureTtsProvider(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AzureTtsProvider>>(),
            configuration["Azure:Tts:ApiKey"],
            configuration["Azure:Tts:Region"] ?? "westeurope"
        ));
        services.AddHttpClient("elevenlabs-tts");
        services.AddSingleton<ITtsProvider>(sp => new ElevenLabsTtsProvider(
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ElevenLabsTtsProvider>>(),
            configuration["ElevenLabs:ApiKey"]
        ));
        services.AddSingleton<ITtsService, TtsService>();

        // Spotify HTTP clients with resilience
        services.AddHttpClient("spotify").AddSpotifyResilienceHandler();
        services.AddHttpClient("spotify-auth");

        // Music providers
        services.AddScoped<SpotifyMusicProvider>();
        services.AddScoped<YouTubeMusicProvider>();
        services.AddScoped<IMusicService, MusicService>();

        // ChannelRegistry (singleton + hosted service)
        services.AddSingleton<NomNomzBot.Domain.Platform.Interfaces.IChannelRegistry, ChannelRegistry>();
        services.AddHostedService(sp =>
            (ChannelRegistry)sp.GetRequiredService<NomNomzBot.Domain.Platform.Interfaces.IChannelRegistry>()
        );

        // Background lifecycle services
        services.AddHostedService<BotLifecycleService>();
        services.AddHostedService<TimerService>();
        services.AddHostedService<DefaultCommandSeederService>();

        // Twitch options
        services.Configure<TwitchOptions>(configuration.GetSection(TwitchOptions.SectionName));

        // Twitch HTTP clients with resilience
        services.AddHttpClient("twitch-auth");
        services.AddHttpClient("twitch-helix").AddTwitchResilienceHandler();
        services.AddHttpClient("twitch-eventsub")
            .ConfigureHttpClient((sp, client) =>
            {
                TwitchOptions opts = sp.GetRequiredService<IOptions<TwitchOptions>>().Value;
                if (!string.IsNullOrEmpty(opts.ClientId))
                    client.DefaultRequestHeaders.Add("Client-Id", opts.ClientId);
            });

        // Twitch auth service (scoped — uses IApplicationDbContext)
        services.AddScoped<ITwitchAuthService, TwitchAuthService>();

        // Twitch API service (scoped — uses IApplicationDbContext for tokens)
        services.AddScoped<ITwitchApiService, TwitchApiService>();

        // Chat provider (Helix-first, used by pipeline actions and background services)
        services.AddScoped<IChatProvider, HelixChatProvider>();

        // Twitch IRC chat service (singleton + hosted service — persistent WebSocket connection)
        services.AddSingleton<TwitchIrcService>();
        services.AddSingleton<ITwitchChatService>(sp => sp.GetRequiredService<TwitchIrcService>());
        services.AddHostedService(sp => sp.GetRequiredService<TwitchIrcService>());

        // Twitch EventSub service (singleton + hosted service — persistent WebSocket connection)
        services.AddSingleton<TwitchEventSubService>();
        services.AddSingleton<ITwitchEventSubService>(sp =>
            sp.GetRequiredService<TwitchEventSubService>()
        );
        services.AddHostedService(sp => sp.GetRequiredService<TwitchEventSubService>());

        return services;
    }

    /// <summary>
    /// Registers additional IEventHandler implementations from external assemblies.
    /// Call this from Application or other layers to register their handlers.
    /// </summary>
    public static IServiceCollection AddEventHandlersFromAssembly(
        this IServiceCollection services,
        Assembly assembly
    )
    {
        RegisterEventHandlers(services, assembly);
        return services;
    }

    /// <summary>
    /// Scans assemblies for IEventHandler implementations and registers them as transient.
    /// </summary>
    private static void RegisterEventHandlers(
        IServiceCollection services,
        params Assembly[] assemblies
    )
    {
        Type handlerInterfaceType = typeof(IEventHandler<>);

        foreach (Assembly assembly in assemblies)
        {
            IEnumerable<Type> handlerTypes = assembly
                .GetTypes()
                .Where(t => t is { IsAbstract: false, IsInterface: false })
                .Where(t =>
                    t.GetInterfaces()
                        .Any(i =>
                            i.IsGenericType && i.GetGenericTypeDefinition() == handlerInterfaceType
                        )
                );

            foreach (Type handlerType in handlerTypes)
            {
                IEnumerable<Type> handlerInterfaces = handlerType
                    .GetInterfaces()
                    .Where(i =>
                        i.IsGenericType && i.GetGenericTypeDefinition() == handlerInterfaceType
                    );

                foreach (Type @interface in handlerInterfaces)
                {
                    services.AddTransient(@interface, handlerType);
                }
            }
        }
    }
}

/// <summary>
/// Service for running EF Core migrations at startup (development only).
/// </summary>
public interface IDatabaseMigrator
{
    Task MigrateAsync(CancellationToken cancellationToken);
}

public sealed class DatabaseMigrator(AppDbContext dbContext) : IDatabaseMigrator
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
