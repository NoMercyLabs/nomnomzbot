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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Eventing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.RateLimiting;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Services;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Tts.Interfaces;
using NomNomzBot.Infrastructure.BackgroundServices;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Commands.Jobs;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Platform.Auth;
using NomNomzBot.Infrastructure.Platform.Caching;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Platform.Persistence.Interceptors;
using NomNomzBot.Infrastructure.Platform.Persistence.Repositories;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.RateLimiting;
using NomNomzBot.Infrastructure.Platform.Resilience;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Platform.Transport;
using NomNomzBot.Infrastructure.Platform.Transport.Helix;
using NomNomzBot.Infrastructure.Tts;

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
                        Microsoft
                            .EntityFrameworkCore
                            .Diagnostics
                            .CoreEventId
                            .PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning
                    )
                );
            }
        );

        services.AddScoped<IApplicationDbContext>(provider =>
            provider.GetRequiredService<AppDbContext>()
        );

        // EventBus (singleton -- resolves scoped handlers internally via IServiceProvider).
        // IEventBus is the JournalingEventBusDecorator over the concrete EventBus: every publish is captured to
        // the event store journal and post-commit hooks fire before delegation to live handlers (event-store §7).
        services.AddSingleton<EventLogger>();
        services.AddSingleton<EventBus>();
        services.AddSingleton<NomNomzBot.Domain.Platform.Interfaces.IEventBus>(
            sp => new NomNomzBot.Infrastructure.EventStore.JournalingEventBusDecorator(
                sp.GetRequiredService<EventBus>(),
                sp,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<NomNomzBot.Infrastructure.EventStore.JournalingEventBusDecorator>>()
            )
        );

        // ── Auto-discovery (D5, backend-structure §4) ────────────────────────
        // One hand-rolled reflection scan binds every pluggable marker by convention.
        // Drop a file → it is live next boot; no marker is added to a DI list by hand.
        Assembly infrastructure = typeof(DependencyInjection).Assembly;

        // Domain event handlers (scoped — per-event work, resolved by EventBus per scope).
        services.AddOpenGenericHandlers(
            infrastructure,
            typeof(IEventHandler<>),
            ServiceLifetime.Scoped
        );

        // Pipeline actions + conditions (transient — stateless strategies, multi-binding).
        services.AddImplementationsOf<ICommandAction>(infrastructure, ServiceLifetime.Transient);
        services.AddImplementationsOf<ICommandCondition>(infrastructure, ServiceLifetime.Transient);

        // Music providers (scoped — multi-binding consumed as IEnumerable<IMusicProvider>).
        services.AddImplementationsOf<IMusicProvider>(infrastructure, ServiceLifetime.Scoped);

        // Webhook HMAC primitives (stateless; not name-convention "*Service", so registered explicitly).
        services.AddSingleton<
            NomNomzBot.Application.Contracts.Webhooks.IInboundSignatureVerifier,
            NomNomzBot.Infrastructure.Webhooks.InboundSignatureVerifier
        >();
        services.AddSingleton<
            NomNomzBot.Application.Contracts.Webhooks.IOutboundWebhookSigner,
            NomNomzBot.Infrastructure.Webhooks.OutboundWebhookSigner
        >();

        // Inbound webhook adapters (transient — stateless, multi-binding by WebhookAdapterKind).
        services.AddImplementationsOf<NomNomzBot.Application.Contracts.Webhooks.IInboundWebhookAdapter>(
            infrastructure,
            ServiceLifetime.Transient
        );

        // The single SSRF-hardened egress client (sandbox + outbound webhooks): resolve-then-pin + https-only.
        services
            .AddHttpClient(NomNomzBot.Infrastructure.Sandbox.EgressHttpClient.Name)
            .ConfigurePrimaryHttpMessageHandler(
                NomNomzBot.Infrastructure.Sandbox.EgressHttpClient.CreateHandler
            )
            .AddHttpMessageHandler(() =>
                new NomNomzBot.Infrastructure.Sandbox.EgressSchemeHandler()
            );

        // Webhook dispatchers (I*Dispatcher — not the name-convention "*Service", so registered explicitly).
        services.AddScoped<
            NomNomzBot.Application.Contracts.Webhooks.IInboundWebhookDispatcher,
            NomNomzBot.Infrastructure.Webhooks.InboundWebhookDispatcher
        >();
        services.AddScoped<
            NomNomzBot.Application.Contracts.Webhooks.IOutboundWebhookDispatcher,
            NomNomzBot.Infrastructure.Webhooks.OutboundWebhookDispatcher
        >();
        services.AddScoped<
            NomNomzBot.Application.Contracts.Billing.IStripeWebhookHandler,
            NomNomzBot.Infrastructure.Billing.StripeWebhookHandler
        >();

        // Outbound webhook retry drain (scoped processor + the hosted worker that ticks it).
        services.AddScoped<NomNomzBot.Infrastructure.Webhooks.WebhookRetryProcessor>();
        services.AddHostedService<NomNomzBot.Infrastructure.BackgroundServices.WebhookDeliveryWorker>();

        // Analytics — the shared viewer-as-User resolver the per-viewer projections fold through.
        services.AddScoped<NomNomzBot.Infrastructure.Analytics.ViewerResolver>();

        // Event store — projections, post-commit hooks, and upcasters are pluggable multi-bindings discovered
        // by convention (drop a file → it is live next boot), mirroring ICommandAction. Projections + hooks
        // touch the DbContext (scoped); upcasters are pure/stateless (singleton).
        services.AddImplementationsOf<NomNomzBot.Application.Contracts.EventStore.IProjection>(
            infrastructure,
            ServiceLifetime.Scoped
        );
        services.AddImplementationsOf<NomNomzBot.Application.Contracts.EventStore.IJournalPostCommitHook>(
            infrastructure,
            ServiceLifetime.Scoped
        );
        services.AddImplementationsOf<NomNomzBot.Application.Contracts.EventStore.IEventUpcaster>(
            infrastructure,
            ServiceLifetime.Singleton
        );

        // Content seeders (scoped — multi-binding consumed as IEnumerable<ISeeder> by the
        // SeedRunner, which orders them by ISeeder.Order and runs them in one transaction).
        services.AddImplementationsOf<ISeeder>(infrastructure, ServiceLifetime.Scoped);

        // Service impls bound by their I<X>Service interface (scoped). Singletons,
        // deployment-variant, and special-construction interfaces stay explicit below
        // and are excluded so the scan never picks a wrong binding. Ambiguity (two
        // impls of one interface) throws at build time — the DeploymentProfile case.
        services.AddServicesByConvention(
            infrastructure,
            ServiceLifetime.Scoped,
            typeof(IJwtTokenService), // singleton crypto
            typeof(ICacheService), // deployment-variant: Redis vs in-memory (ambiguity)
            typeof(ITrustService), // singleton
            typeof(ITtsService), // singleton — stateful TTS queues
            typeof(ITwitchChatService), // singleton + hosted (shared TwitchIrcService instance)
            typeof(ITwitchEventSubService), // singleton + hosted (shared TwitchEventSubHostedService instance)
            typeof(ISubjectKeyService) // crypto envelope — wired explicitly below
        );

        // Repositories (scoped — concrete GenericRepository<T> subclasses, consumed by type).
        services.AddRepositoriesByConvention<GenericRepository<object>>(
            infrastructure,
            ServiceLifetime.Scoped
        );

        // Hosted workers (singleton — long-lived BackgroundService/IHostedService). The three
        // singleton+hosted services below share one instance with their service interface, so
        // they are wired explicitly and excluded here. This auto-wires TokenRefreshService.
        services.AddHostedWorkers(
            infrastructure,
            typeof(TwitchIrcService),
            typeof(TwitchEventSubHostedService),
            typeof(ChannelRegistry)
        );

        // Security — envelope encryption (gdpr-crypto spec). Field cipher = AES-256-GCM + AAD over a
        // per-subject DEK; the DEK is wrapped by the deployment KEK held in an OS-native secure store
        // (Windows DPAPI) or derived from Encryption:Key (deterministic CI/dev/headless fallback).
        services.Configure<EncryptionOptions>(
            configuration.GetSection(EncryptionOptions.SectionName)
        );

        // Crypto primitives (stateless, singleton — in-box System.Security.Cryptography).
        services.AddSingleton<IFieldCipher, AesGcmFieldCipher>();

        // KEK-custody adapter (local_aes — OS-native secure store / deterministic config-key fallback).
        // The kms_envelope (Azure Managed-HSM) branch is the SaaS profile variant and is wired there.
        services.AddSingleton<IKeyVault, OsSecureStoreKeyVault>();

        // DEK registry — in-process store backs the contract until the CryptoKey EF table + Guid
        // BroadcasterId widening land (schema build-dependency); singleton so DEKs persist process-wide.
        services.AddSingleton<ISubjectKeyStore, InMemorySubjectKeyStore>();

        // DEK lifecycle + token-protection facade (singleton — stateless over the singleton store/vault).
        services.AddSingleton<ISubjectKeyService, SubjectKeyService>();
        services.AddSingleton<ITokenProtector, TokenProtector>();

        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        // OAuth token vault (identity-auth §3.4) — scoped (DbContext); not I<X>Service-named, so explicit.
        services.AddScoped<
            NomNomzBot.Application.Identity.Services.IIntegrationTokenVault,
            NomNomzBot.Infrastructure.Identity.IntegrationTokenVault
        >();

        // Generic OAuth connect (integrations-oauth §3.2) — provider descriptors registry (singleton, stateless).
        services.AddSingleton<
            NomNomzBot.Application.Integrations.Services.IOAuthProviderRegistry,
            NomNomzBot.Infrastructure.Integrations.OAuthProviderRegistry
        >();
        // The descriptor-driven connect flow's token-exchange / account-identity HTTP client.
        services.AddHttpClient("integration-oauth");

        // ISessionService, IScopeGrantService, IIntegrationOAuthService, IAuthService follow the
        // I<X>Service single-impl convention and are bound scoped by AddServicesByConvention above.

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

        // The single clock (platform-conventions §3.11): every service / handler /
        // BackgroundService that reads the current time injects TimeProvider and calls
        // GetUtcNow(). TimeProvider.System is the real clock; tests inject FakeTimeProvider.
        services.AddSingleton(TimeProvider.System);

        // Singleton services (stateful / crypto / engine primitives) — kept explicit:
        // these are NOT per-request scoped, so the convention scan excludes their interfaces.
        services.AddSingleton<ICooldownManager, CooldownManager>();
        services.AddSingleton<ITemplateEngine, TemplateEngine>();
        services.AddSingleton<ITemplateResolver, TemplateResolver>();
        services.AddSingleton<ITrustService, TrustService>();
        // Game outcome RNG (stateless CSPRNG; not an I<X>Service, so registered explicitly).
        services.AddSingleton<
            NomNomzBot.Application.Economy.Services.IGameRandomizer,
            NomNomzBot.Infrastructure.Economy.CsprngGameRandomizer
        >();

        // PipelineEngine (scoped — IPipelineEngine is not an I<X>Service, registered explicitly)
        services.AddScoped<IPipelineEngine, PipelineEngine>();

        // Identity / tenant — HttpContext accessor + UnitOfWork are framework/infra, kept explicit.
        // The I<X>Service impls (ICurrentTenantService, IChannelAccessService, ICurrentUserService,
        // IAdminService, ICommandService, IChannelService, IRewardService, IWidgetService,
        // IUserService, IModerationService, IAuthService, IPermissionService, ITimerManagementService,
        // IMusicConfigService, ITtsConfigService, IEventResponseService, IPipelineService,
        // IFeatureService, IGdprService) are now discovered by AddServicesByConvention above.
        services.AddHttpContextAccessor();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Startup helpers consumed by concrete type (not pluggable markers) — kept explicit.
        services.AddTransient<StartupReadinessChecker>();
        services.AddScoped<IDatabaseMigrator, DatabaseMigrator>();
        services.AddScoped<SeedRunner>();
        services.AddScoped<SqliteMigrationService>();

        // Auto-moderation engine consumed by concrete type — kept explicit.
        services.AddScoped<AutoModerationEngine>();

        // TTS providers (singleton, multi-binding) — special construction with config args,
        // kept explicit. ITtsService is singleton (stateful queues), also kept explicit.
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

        // Spotify HTTP clients with resilience (Music providers themselves are scanned by
        // IMusicProvider above; IMusicService is scanned by AddServicesByConvention).
        services.AddHttpClient("spotify").AddSpotifyResilienceHandler();
        services.AddHttpClient("spotify-auth");

        // ChannelRegistry (singleton + hosted service — one instance serves IChannelRegistry
        // AND the hosted lifecycle, so it is wired explicitly and excluded from the worker scan).
        services.AddSingleton<
            NomNomzBot.Domain.Platform.Interfaces.IChannelRegistry,
            ChannelRegistry
        >();
        services.AddHostedService(sp =>
            (ChannelRegistry)
                sp.GetRequiredService<NomNomzBot.Domain.Platform.Interfaces.IChannelRegistry>()
        );

        // BotLifecycleService, TimerService, and TokenRefreshService are auto-registered as
        // hosted services by AddHostedWorkers above. Content seeding is no longer a hosted
        // service — it runs once at startup via SeedRunner (backend-structure §5.1).

        // Twitch options
        services.Configure<TwitchOptions>(configuration.GetSection(TwitchOptions.SectionName));

        // Twitch HTTP clients with resilience
        services.AddHttpClient("twitch-auth");

        // ── Helix transport plumbing (twitch-helix.md §3, §7) ────────────────
        // The named "twitch-helix" client carries the full Helix request pipeline:
        //   resilience (retry+breaker+timeout, no 4xx retry) → adaptive header-driven rate limiter →
        //   Client-Id + bearer injection. Order is outermost-first; the auth/limit handlers run closest
        //   to the wire so they see the real status + Ratelimit-* headers. The transport itself resolves
        //   the per-call token and stows it on the request options for the handlers.
        services.AddTransient<TwitchAuthHeaderHandler>();
        services.AddTransient<TwitchRateLimitHandler>();
        services
            .AddHttpClient("twitch-helix")
            .AddTwitchResilienceHandler()
            .AddHttpMessageHandler<TwitchRateLimitHandler>()
            .AddHttpMessageHandler<TwitchAuthHeaderHandler>();

        // Adaptive rate limiter — singleton (per-token buckets survive requests). Self-host binds the
        // in-process limiter; the SaaS multi-node variant (delegating to the cross-node IRateLimiter,
        // scaling-qos §4) is an additive adapter that lands with that subsystem.
        services.AddSingleton<ITwitchRateLimiter, TwitchRateLimiter>();

        // Token resolver (scoped — reads Services via the scoped DbContext, refreshes via the auth layer).
        services.AddScoped<ITwitchTokenResolver, TwitchTokenResolver>();

        // The DTO-agnostic Helix send pipeline every codegen-fed per-endpoint method rides on (scoped).
        services.AddScoped<ITwitchHelixTransport, TwitchHelixTransport>();

        // ── Helix sub-clients (twitch-helix.md §3) — thin, uniform per-category wrappers over the
        // transport, all scoped. Each resolves the tenant Guid → Twitch id, pre-checks scopes, and maps
        // the typed response; local-state sync + domain events stay in the consuming services (SRP).
        services.AddScoped<
            ITwitchChannelsApi,
            Platform.Transport.Helix.SubClients.TwitchChannelsApi
        >();
        services.AddScoped<ITwitchUsersApi, Platform.Transport.Helix.SubClients.TwitchUsersApi>();
        services.AddScoped<
            ITwitchChannelPointsApi,
            Platform.Transport.Helix.SubClients.TwitchChannelPointsApi
        >();
        services.AddScoped<
            ITwitchStreamsApi,
            Platform.Transport.Helix.SubClients.TwitchStreamsApi
        >();
        services.AddScoped<
            ITwitchSubscriptionsApi,
            Platform.Transport.Helix.SubClients.TwitchSubscriptionsApi
        >();
        services.AddScoped<ITwitchSearchApi, Platform.Transport.Helix.SubClients.TwitchSearchApi>();
        services.AddScoped<
            ITwitchModerationApi,
            Platform.Transport.Helix.SubClients.TwitchModerationApi
        >();
        services.AddScoped<
            ITwitchModeratorsApi,
            Platform.Transport.Helix.SubClients.TwitchModeratorsApi
        >();
        services.AddScoped<ITwitchPollsApi, Platform.Transport.Helix.SubClients.TwitchPollsApi>();
        services.AddScoped<
            ITwitchPredictionsApi,
            Platform.Transport.Helix.SubClients.TwitchPredictionsApi
        >();
        services.AddScoped<ITwitchRaidsApi, Platform.Transport.Helix.SubClients.TwitchRaidsApi>();
        services.AddScoped<ITwitchChatApi, Platform.Transport.Helix.SubClients.TwitchChatApi>();
        services.AddScoped<
            ITwitchChatAssetsApi,
            Platform.Transport.Helix.SubClients.TwitchChatAssetsApi
        >();
        services.AddScoped<ITwitchBitsApi, Platform.Transport.Helix.SubClients.TwitchBitsApi>();
        services.AddScoped<ITwitchClipsApi, Platform.Transport.Helix.SubClients.TwitchClipsApi>();
        services.AddScoped<ITwitchVideosApi, Platform.Transport.Helix.SubClients.TwitchVideosApi>();
        services.AddScoped<
            ITwitchScheduleApi,
            Platform.Transport.Helix.SubClients.TwitchScheduleApi
        >();
        services.AddScoped<ITwitchAdsApi, Platform.Transport.Helix.SubClients.TwitchAdsApi>();
        services.AddScoped<
            ITwitchCharityApi,
            Platform.Transport.Helix.SubClients.TwitchCharityApi
        >();
        services.AddScoped<ITwitchGoalsApi, Platform.Transport.Helix.SubClients.TwitchGoalsApi>();
        services.AddScoped<
            ITwitchHypeTrainApi,
            Platform.Transport.Helix.SubClients.TwitchHypeTrainApi
        >();
        services.AddScoped<ITwitchTeamsApi, Platform.Transport.Helix.SubClients.TwitchTeamsApi>();
        services.AddScoped<ITwitchGamesApi, Platform.Transport.Helix.SubClients.TwitchGamesApi>();
        services.AddScoped<
            ITwitchContentClassificationApi,
            Platform.Transport.Helix.SubClients.TwitchContentClassificationApi
        >();
        services.AddScoped<
            ITwitchWhispersApi,
            Platform.Transport.Helix.SubClients.TwitchWhispersApi
        >();
        services.AddScoped<
            ITwitchGuestStarApi,
            Platform.Transport.Helix.SubClients.TwitchGuestStarApi
        >();

        // Top-level façade (twitch-helix.md §3.1) — composes the scoped sub-clients above into one
        // named-accessor surface for discoverability. Pure passthrough, scoped to share their lifetime.
        services.AddScoped<ITwitchHelixClient, Platform.Transport.Helix.TwitchHelixClient>();

        services
            .AddHttpClient("twitch-eventsub")
            .ConfigureHttpClient(
                (sp, client) =>
                {
                    TwitchOptions opts = sp.GetRequiredService<IOptions<TwitchOptions>>().Value;
                    if (!string.IsNullOrEmpty(opts.ClientId))
                        client.DefaultRequestHeaders.Add("Client-Id", opts.ClientId);
                }
            );

        // ITwitchAuthService → TwitchAuthService is a scoped single-impl service discovered by
        // AddServicesByConvention above. (The legacy ITwitchApiService has been retired — every caller now
        // targets the granular Helix sub-clients / ITwitchHelixClient façade.)

        // Roles & permissions — the effective-level resolver (Gate-2 reads it). Not an I<X>Service, so it is
        // registered explicitly rather than by AddServicesByConvention. Scoped (reads the per-request DbContext).
        services.AddScoped<
            NomNomzBot.Application.Contracts.Authorization.IRoleResolver,
            Identity.RoleResolver
        >();

        // Twitch identity resolver — the single seam translating tenant/user Guids ↔ Twitch string ids
        // (the invariant: Twitch never receives a Guid). Scoped: reads the per-request DbContext.
        services.AddScoped<ITwitchIdentityResolver, TwitchIdentityResolver>();

        // Chat provider (Helix-first, used by pipeline actions and background services).
        // IChatProvider is not an I<X>Service, so it is registered explicitly.
        services.AddScoped<IChatProvider, HelixChatProvider>();

        // Twitch IRC chat service (singleton + hosted service — persistent WebSocket connection)
        services.AddSingleton<TwitchIrcService>();
        services.AddSingleton<ITwitchChatService>(sp => sp.GetRequiredService<TwitchIrcService>());
        services.AddHostedService(sp => sp.GetRequiredService<TwitchIrcService>());

        // ── Twitch EventSub (twitch-eventsub §7) ─────────────────────────────
        // Per-topic create facts (condition/version/token-owner) — pure, singleton.
        services.AddSingleton<IEventSubConditionBuilder, EventSubConditionBuilder>();

        // Typed fan-out (§3.7): one IEventSubEventTranslator per subscription type, auto-discovered (drop a file
        // → it is live next boot, no DI list to edit), indexed by the singleton registry the dispatcher resolves.
        services.AddImplementationsOf<IEventSubEventTranslator>(
            infrastructure,
            ServiceLifetime.Singleton
        );
        services.AddSingleton<IEventSubTranslatorRegistry, EventSubTranslatorRegistry>();

        // The notification dispatcher is the single dedupe + journal + fan-out path both transports call.
        // Scoped: journals via the scoped IEventJournal (DbContext + IUnitOfWork).
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

        // Transport is profile-selected (scaling-qos §6). Self-host/lite → WebSocket (ClientWebSocket).
        // The SaaS conduit+webhook transport and its EventSubWebhookController / IWebhookSignatureVerifier are
        // DEFERRED — they depend on the SaaS deployment-profile axis + conduit provisioner that do not exist
        // yet (the seam IEventSubTransport admits the second impl additively, no rewrite). Self-host is the
        // only profile today, so the WebSocket transport is wired unconditionally.
        services.AddSingleton<IWebSocketChannelFactory, ClientWebSocketChannelFactory>();
        services.AddSingleton<IEventSubTransport, WebSocketEventSubTransport>();

        // The lifecycle host: one instance behind ITwitchEventSubService + IEventSource + IHostedService.
        services.AddSingleton<TwitchEventSubHostedService>();
        services.AddSingleton<ITwitchEventSubService>(sp =>
            sp.GetRequiredService<TwitchEventSubHostedService>()
        );
        services.AddSingleton<NomNomzBot.Application.Contracts.Platform.IEventSource>(sp =>
            sp.GetRequiredService<TwitchEventSubHostedService>()
        );
        services.AddHostedService(sp => sp.GetRequiredService<TwitchEventSubHostedService>());

        // ── Event store (event-store §7) ─────────────────────────────────────
        // Journal, allocator, subscriber, projection runner all touch the DbContext / IUnitOfWork (scoped).
        // The upcaster registry is pure over the singleton upcaster set (singleton). EventJournalRepository is
        // self-registered scoped by AddRepositoriesByConvention above (it derives from GenericRepository<T>).
        services.AddScoped<
            NomNomzBot.Application.Contracts.EventStore.ITenantSequenceAllocator,
            NomNomzBot.Infrastructure.EventStore.TenantSequenceAllocator
        >();
        services.AddScoped<
            NomNomzBot.Application.Contracts.EventStore.IEventJournal,
            NomNomzBot.Infrastructure.EventStore.EventJournalService
        >();
        services.AddScoped<
            NomNomzBot.Application.Contracts.EventStore.IEventStoreSubscriber,
            NomNomzBot.Infrastructure.EventStore.EventStoreSubscriber
        >();
        services.AddScoped<
            NomNomzBot.Application.Contracts.EventStore.IProjectionRunner,
            NomNomzBot.Infrastructure.EventStore.ProjectionRunner
        >();
        services.AddSingleton<
            NomNomzBot.Application.Contracts.EventStore.IEventUpcasterRegistry,
            NomNomzBot.Infrastructure.EventStore.EventUpcasterRegistry
        >();

        return services;
    }

    /// <summary>
    /// Discovers <see cref="IEventHandler{TEvent}"/> implementations in <paramref name="assembly"/>
    /// (e.g. the Api layer's hub broadcasters) and registers them scoped, via the same
    /// assembly-scan convention used for the Infrastructure handlers (backend-structure §4).
    /// </summary>
    public static IServiceCollection AddEventHandlersFromAssembly(
        this IServiceCollection services,
        Assembly assembly
    ) => services.AddOpenGenericHandlers(assembly, typeof(IEventHandler<>), ServiceLifetime.Scoped);
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
