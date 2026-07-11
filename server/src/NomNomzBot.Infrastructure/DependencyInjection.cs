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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Abstractions.Content;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.RateLimiting;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Application.Services;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Tts.Interfaces;
using NomNomzBot.Infrastructure.BackgroundServices;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Chat.YouTube;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Integrations.YouTube;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Platform.Auth;
using NomNomzBot.Infrastructure.Platform.Caching;
using NomNomzBot.Infrastructure.Platform.Deployment;
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

        // ── Deployment profile (platform-conventions §3.3, deployment-distribution §2) ────────
        // Resolve the deployment mode ONCE, here at registration time (before the host is built), so every
        // provider-specific adapter below is DI-selected from it — the DB provider, cache, bus, KEK, run-once
        // guard, and rate-limiter store. The mode is forced by config/env (Deployment:Mode / App:DeploymentMode)
        // or auto-detected by probing Postgres + Redis reachability (both up ⇒ full, else lite). The single-row
        // DeploymentProfile (P.12) is persisted + the resolved event emitted at boot by DeploymentProfileService.
        (DeploymentMode mode, bool _) = DeploymentModeResolver.Resolve(configuration);
        DbProviderKind dbProvider = DeploymentModeResolver.DbProviderFor(mode);
        CacheProviderKind cacheProvider = DeploymentModeResolver.CacheProviderFor(mode);

        // Expose the resolved mode so services can branch on it without re-resolving — e.g. the self-host
        // first-owner admin bootstrap (the owner == the admin only on self-host).
        services.AddSingleton(new DeploymentContext(mode));

        services.AddSingleton<IInfraReachabilityProbe, InfraReachabilityProbe>();
        services.AddSingleton<IDeploymentProfileService, DeploymentProfileService>();

        // The bound-listen-port carrier (deployment-distribution §6): the Api host resolves the actual port (smart
        // self-host port handling) before binding and publishes it here; the self-host mDNS advertiser reads it so
        // the LAN announcement carries the real port. Always registered (the Api sets it on every profile); only the
        // advertiser that consumes it is self-host-gated below.
        services.AddSingleton<IListenEndpointAccessor, ListenEndpointAccessor>();

        // mDNS / DNS-SD LAN advertiser (deployment-distribution §6) — SELF-HOST ONLY. A cloud bot has no LAN to be
        // discovered on, so on SaaS the service is simply not added (no no-op shim). It is excluded from the hosted
        // worker auto-scan below so this profile gate is the single place that decides whether it runs.
        if (mode is DeploymentMode.SelfHostLite or DeploymentMode.SelfHostFull)
            services.AddHostedService<MdnsAdvertiserHostedService>();

        // DbContext provider — SQLite (lite, a file beside the binary) or Npgsql (full/SaaS). The interceptors +
        // the query-filter warning suppression are identical on both; only the provider + its migration set differ.
        string? connectionString =
            configuration.GetConnectionString("DefaultConnection")
            ?? configuration.GetSection("Database:ConnectionString").Value;
        string sqliteConnectionString =
            configuration.GetConnectionString("SqliteConnection")
            ?? SelfHostDataPaths.SqliteConnectionString;

        // Build the Npgsql data source once and register it as a singleton so there is exactly one
        // connection pool for the lifetime of the application. Building it inside AddDbContext's
        // options factory would create a new pool on every DbContext scope (per request), quickly
        // exhausting Postgres' max_connections.
        if (dbProvider == DbProviderKind.Postgres && connectionString is not null)
        {
            Npgsql.NpgsqlDataSourceBuilder dataSourceBuilder = new(connectionString);
            dataSourceBuilder.EnableDynamicJson();
            services.AddSingleton(dataSourceBuilder.Build());
        }

        services.AddDbContext<AppDbContext>(
            (serviceProvider, options) =>
            {
                if (dbProvider == DbProviderKind.Sqlite)
                {
                    options.UseSqlite(
                        sqliteConnectionString,
                        sqliteOptions =>
                        {
                            sqliteOptions.MigrationsAssembly("NomNomzBot.Migrations.Sqlite");
                        }
                    );
                }
                else
                {
                    Npgsql.NpgsqlDataSource dataSource =
                        serviceProvider.GetRequiredService<Npgsql.NpgsqlDataSource>();

                    options.UseNpgsql(
                        dataSource,
                        npgsqlOptions =>
                        {
                            npgsqlOptions.MigrationsAssembly(
                                typeof(AppDbContext).Assembly.FullName
                            );
                        }
                    );
                }

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

        // Run-once guard (platform-conventions §3.8) — no-op on self-host (single process), pg advisory lock on
        // SaaS. Rate-limiter counter store (§3.7) — in-memory per-instance on lite, Redis cluster-wide on full/SaaS.
        if (mode == DeploymentMode.Saas)
            services.AddSingleton<IRunOnceGuard, PostgresRunOnceGuard>();
        else
            services.AddSingleton<IRunOnceGuard, NoOpRunOnceGuard>();

        // EventBus (singleton -- resolves scoped handlers internally via IServiceProvider).
        // IEventBus is the JournalingEventBusDecorator over the concrete EventBus: every publish is captured to
        // the event store journal and post-commit hooks fire before delegation to live handlers (event-store §7).
        services.AddSingleton<EventLogger>();
        services.AddSingleton<EventBus>();
        services.AddSingleton<IEventBus>(sp => new EventStore.JournalingEventBusDecorator(
            sp.GetRequiredService<EventBus>(),
            sp,
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<EventStore.JournalingEventBusDecorator>>()
        ));

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

        // §3.10 manage surface (music-sr.md) — capability-gating front over the registered providers;
        // scoped because it delegates to the scoped IMusicProvider instances. Explicit registration:
        // the interface does not match the I<X>Service convention scan.
        services.AddScoped<
            Application.Contracts.Music.IMusicProviderManageApi,
            Music.MusicProviderManageApi
        >();

        // Runtime-observed integration capabilities (e.g. spotify.premium from a player-403) —
        // singleton observation cache feeding IntegrationStatusDto.Capabilities. Explicit: the
        // interface does not match the I<X>Service convention scan.
        services.AddSingleton<
            Application.Integrations.Services.IIntegrationCapabilityStore,
            Integrations.InMemoryIntegrationCapabilityStore
        >();

        // Third-party emote providers (singleton — stateless HTTP-fetch adapters, multi-bound + registry-indexed).
        services.AddImplementationsOf<Application.Chat.Services.IThirdPartyEmoteProvider>(
            infrastructure,
            ServiceLifetime.Singleton
        );
        services.AddSingleton<
            Application.Chat.Services.IThirdPartyEmoteProviderRegistry,
            ThirdPartyEmoteProviderRegistry
        >();
        // Warms the third-party emote cache the decoration pipeline reads; driven by ChatDecorationRefreshService
        // (auto-discovered as a hosted worker). Stateless → singleton.
        services.AddSingleton<Chat.Jobs.ChatEmoteCacheWarmer>();
        // Warms the Helix badge + cheermote caches; scoped (they use the scoped Helix client — resolved per worker scope).
        services.AddScoped<Chat.Jobs.ChatBadgeCacheWarmer>();
        services.AddScoped<Chat.Jobs.ChatCheermoteCacheWarmer>();

        // Chat-decoration pipeline: the ordered adapters (multi-binding, discovered) + the thin orchestrator that runs
        // them per message. Scoped to match the per-message orchestrator and the scoped services some adapters use
        // (the link-preview adapter resolves the scoped ILinkPreviewService); the adapters are stateless.
        services.AddImplementationsOf<Application.Chat.Services.IChatDecorationAdapter>(
            infrastructure,
            ServiceLifetime.Scoped
        );
        // Badge + cheermote resolvers (cache-only; not name-convention "*Service", so registered explicitly). Singleton.
        services.AddSingleton<Application.Chat.Services.IChatBadgeResolver, ChatBadgeResolver>();
        services.AddSingleton<
            Application.Chat.Services.ICheermoteResolver,
            ChatCheermoteResolver
        >();
        // Per-channel chat-colour memory backing the mention-colour step (cache-only). Stateless → singleton.
        services.AddSingleton<Application.Chat.Services.IChatColorMemory, ChatColorMemory>();
        // Scoped: it resolves the channel's feature toggles through the scoped IFeatureService (cache-backed, so the
        // hot path stays cheap). Consumes the singleton adapters + cache fine.
        services.AddScoped<Application.Chat.Services.IChatMessageDecorator, ChatMessageDecorator>();
        // Operator chat sender (scoped — resolves the operator identity via ITwitchIdentityResolver; chat-client.md §3.3).
        services.AddScoped<Application.Chat.Services.IOperatorChatSender, OperatorChatSender>();
        // Operator message deleter (scoped — deletes AS the operator so Twitch attributes it to them; chat-client.md §3.5).
        // Not an I<X>Service, so it is registered explicitly rather than by AddServicesByConvention.
        services.AddScoped<
            Application.Moderation.Services.IOperatorMessageDeleter,
            Moderation.OperatorMessageDeleter
        >();
        // Composer emote catalogue (scoped — reads the warm decoration cache + fetches Twitch emotes; chat-client.md §3.2).
        services.AddScoped<Application.Chat.Services.IChatEmoteCatalogue, ChatEmoteCatalogue>();
        // Every outbound HttpClient the factory builds (provider fetches, OAuth, Twitch, TTS, webhooks…) sends
        // the product User-Agent by default, stamped with the running build version. A client may still override.
        services.ConfigureHttpClientDefaults(builder =>
            builder.ConfigureHttpClient(client =>
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    NomNomzBot.Infrastructure.Platform.Http.AppUserAgent.Value
                )
            )
        );

        services
            .AddHttpClient(
                NomNomzBot.Infrastructure.Chat.ChatEmoteHttpClient.Name,
                // Generous overall ceiling; the resilience pipeline owns the per-attempt 10s timeout + retries.
                client => client.Timeout = TimeSpan.FromSeconds(30)
            )
            .AddChatEmoteResilienceHandler();

        // The alejo.io pronoun reference client — fetched once at boot by PronounSeeder to keep the seeded
        // pronoun set current (the resilience pipeline owns the per-attempt 10s timeout + retries).
        services.AddSingleton<
            Application.Identity.Services.IAlejoPronounClient,
            Identity.Providers.AlejoPronounClient
        >();
        services
            .AddHttpClient(
                NomNomzBot.Infrastructure.Identity.AlejoHttpClient.Name,
                client => client.Timeout = TimeSpan.FromSeconds(30)
            )
            .AddAlejoResilienceHandler();

        // Pronoun provider + self-service.
        services.AddSingleton<
            Application.Identity.Services.IPronounProvider,
            Identity.Providers.AlejoPronounProvider
        >();
        services.AddScoped<
            Application.Identity.Services.IPronounResolutionService,
            Identity.Services.PronounResolutionService
        >();
        services.AddScoped<
            Application.Identity.Services.IPronounSelfService,
            Identity.Services.PronounSelfService
        >();

        // Sound clip library (spec §3, §4, §5).
        services.AddScoped<Application.Sound.Services.ISoundClipService, Sound.SoundClipService>();
        services.AddScoped<Application.Sound.Services.ISoundClipStore, Sound.DiskSoundClipStore>();
        // No-op fallback; the API host replaces this with the SignalR-backed SoundClipOverlayNotifierAdapter.
        services.AddScoped<
            Application.Sound.Services.ISoundClipOverlayNotifier,
            Sound.NullSoundClipOverlayNotifier
        >();
        // play_sound + stop_sound auto-discovered via ICommandAction scan.

        // Custom data sources: management CRUD + the single ingest path + auto-discovered presets.
        services.AddScoped<
            Application.CustomEvents.Services.ICustomDataSourceService,
            CustomEvents.CustomDataSourceService
        >();
        services.AddScoped<
            Application.CustomEvents.Services.ICustomDataIngestService,
            CustomEvents.CustomDataIngestService
        >();
        services.AddSingleton<
            Application.CustomEvents.Services.ICustomDataSourcePreset,
            CustomEvents.Presets.PulsoidPreset
        >();
        services.AddSingleton<
            Application.CustomEvents.Services.ICustomDataSourcePreset,
            CustomEvents.Presets.HypeRatePreset
        >();

        // Webhook HMAC primitives (stateless; not name-convention "*Service", so registered explicitly).
        services.AddSingleton<
            Application.Contracts.Webhooks.IInboundSignatureVerifier,
            Webhooks.InboundSignatureVerifier
        >();
        services.AddSingleton<
            Application.Contracts.Webhooks.IOutboundWebhookSigner,
            Webhooks.OutboundWebhookSigner
        >();

        // Inbound webhook adapters (transient — stateless, multi-binding by WebhookAdapterKind).
        services.AddImplementationsOf<Application.Contracts.Webhooks.IInboundWebhookAdapter>(
            infrastructure,
            ServiceLifetime.Transient
        );

        // The single SSRF-hardened egress client (sandbox + outbound webhooks): resolve-then-pin + https-only.
        services
            .AddHttpClient(NomNomzBot.Infrastructure.Sandbox.EgressHttpClient.Name)
            .ConfigurePrimaryHttpMessageHandler(
                NomNomzBot.Infrastructure.Sandbox.EgressHttpClient.CreateHandler
            )
            .AddHttpMessageHandler(() => new Sandbox.EgressSchemeHandler());

        // Webhook dispatchers (I*Dispatcher — not the name-convention "*Service", so registered explicitly).
        services.AddScoped<
            Application.Contracts.Webhooks.IInboundWebhookDispatcher,
            Webhooks.InboundWebhookDispatcher
        >();
        services.AddScoped<
            Application.Contracts.Webhooks.IOutboundWebhookDispatcher,
            Webhooks.OutboundWebhookDispatcher
        >();
        services.AddScoped<
            Application.Contracts.Billing.IStripeWebhookHandler,
            Billing.StripeWebhookHandler
        >();

        // Outbound webhook retry drain (scoped processor + the hosted worker that ticks it).
        services.AddScoped<Webhooks.WebhookRetryProcessor>();
        services.AddHostedService<WebhookDeliveryWorker>();

        // Analytics — the shared viewer-as-User resolver the per-viewer projections fold through.
        services.AddScoped<Analytics.ViewerResolver>();
        // The live-window resolver (watch-session gating; non-"*Service", so registered explicitly).
        services.AddScoped<
            Application.Contracts.Analytics.ILiveWindowResolver,
            Analytics.LiveWindowResolver
        >();

        // The sandbox script executor — Jint on self-host (Wasmtime SaaS adapter is a separate profile binding).
        services.AddScoped<
            Application.Contracts.CustomCode.IScriptExecutor,
            CustomCode.Jint.JintScriptExecutor
        >();
        // The script run orchestrator + capability broker (non-"*Service", so registered explicitly).
        services.AddScoped<
            Application.Contracts.CustomCode.IScriptRunner,
            CustomCode.ScriptRunner
        >();
        services.AddScoped<
            Application.Contracts.CustomCode.IScriptCapabilityBroker,
            CustomCode.ScriptCapabilityBroker
        >();
        services.AddScoped<
            Application.Contracts.CustomCode.IScriptExecutionMeter,
            CustomCode.ScriptExecutionMeter
        >();
        services.AddScoped<Application.Contracts.Billing.IStripeGateway, Billing.StripeGateway>();

        // Event store — projections, post-commit hooks, and upcasters are pluggable multi-bindings discovered
        // by convention (drop a file → it is live next boot), mirroring ICommandAction. Projections + hooks
        // touch the DbContext (scoped); upcasters are pure/stateless (singleton).
        services.AddImplementationsOf<Application.Contracts.EventStore.IProjection>(
            infrastructure,
            ServiceLifetime.Scoped
        );
        services.AddImplementationsOf<Application.Contracts.EventStore.IJournalPostCommitHook>(
            infrastructure,
            ServiceLifetime.Scoped
        );
        services.AddImplementationsOf<Application.Contracts.EventStore.IEventUpcaster>(
            infrastructure,
            ServiceLifetime.Singleton
        );

        // Content seeders (scoped — multi-binding consumed as IEnumerable<ISeeder> by the
        // SeedRunner, which orders them by ISeeder.Order and runs them in one transaction).
        services.AddImplementationsOf<ISeeder>(infrastructure, ServiceLifetime.Scoped);

        // Also resolvable by its own concrete type: DefaultCommandsSeedOnOnboardingHandler injects it
        // directly to seed just the newly-onboarded channel immediately, without waiting for the next
        // full-startup ISeeder pass.
        services.AddScoped<Content.Commands.DefaultCommandsSeeder>();

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
            typeof(ITwitchEventSubService), // singleton + hosted (shared TwitchEventSubHostedService instance)
            typeof(ISubjectKeyService), // crypto envelope — wired explicitly below
            typeof(IDeploymentProfileService) // singleton — boot detector + Current accessor (wired explicitly above)
        );

        // Repositories (scoped — concrete GenericRepository<T> subclasses, consumed by type).
        services.AddRepositoriesByConvention<GenericRepository<object>>(
            infrastructure,
            ServiceLifetime.Scoped
        );

        // Hosted workers (singleton — long-lived BackgroundService/IHostedService). The two
        // singleton+hosted services below share one instance with their service interface, so
        // they are wired explicitly and excluded here. This auto-wires TokenRefreshService.
        services.AddHostedWorkers(
            infrastructure,
            typeof(TwitchEventSubHostedService),
            typeof(ChannelRegistry),
            // Profile-gated above (self-host only) — must not be picked up unconditionally by the worker scan.
            typeof(MdnsAdvertiserHostedService)
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

        // DEK registry — persisted in the CryptoKey table (schema Q.1) so wrapped DEKs survive a restart and a
        // token sealed in one process decrypts in the next. Scoped because it owns the (scoped) DbContext.
        services.AddScoped<ISubjectKeyStore, CryptoKeySubjectKeyStore>();

        // DEK lifecycle + token-protection facade. Scoped (not singleton): they compose the scoped DEK store, so
        // a singleton here would captively pin one DbContext for the process lifetime. The crypto itself is
        // stateless; the only per-request state is the store's DbContext.
        services.AddScoped<ISubjectKeyService, SubjectKeyService>();
        services.AddScoped<ITokenProtector, TokenProtector>();

        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        // OAuth token vault (identity-auth §3.4) — scoped (DbContext); not I<X>Service-named, so explicit.
        services.AddScoped<
            Application.Identity.Services.IIntegrationTokenVault,
            Identity.IntegrationTokenVault
        >();

        // System OAuth-app credential resolver (onboarding keystone) — the single DB-vaulted-first → config
        // path the wizard saves to and the live OAuth flows read from. Singleton: creates its own scope per
        // read (IServiceScopeFactory) so it never shares a DbContext with its caller.
        services.AddSingleton<
            ISystemCredentialsProvider,
            Platform.Configuration.SystemCredentialsProvider
        >();

        // Generic OAuth connect (integrations-oauth §3.2) — provider descriptors registry (singleton, stateless).
        services.AddSingleton<
            Application.Integrations.Services.IOAuthProviderRegistry,
            Integrations.OAuthProviderRegistry
        >();
        // The descriptor-driven connect flow's token-exchange / account-identity HTTP client.
        services.AddHttpClient("integration-oauth");

        // Music-provider token bridge (integrations-oauth → music): after a Spotify/YouTube connect, mirrors the
        // just-vaulted OAuth tokens into the legacy `Service` store the music providers still read from, so a
        // connected music integration is actually usable instead of looping on "reconnect". Scoped — writes
        // through the per-request DbContext. Not an I<X>Service, so it is registered explicitly here.
        services.AddScoped<
            Application.Contracts.Music.IMusicProviderTokenMirror,
            Music.MusicProviderTokenMirror
        >();

        // Login-provider descriptors (platform-identity §3.2) — data, not a fork. Singleton (static list); the
        // feature-flag lookup inside EnabledAsync is resolved through a fresh scope. Not an I<X>Service, so it
        // is excluded from the convention scan and registered explicitly here.
        services.AddSingleton<
            Application.Identity.Services.ILoginProviderRegistry,
            Identity.LoginProviderRegistry
        >();

        // The Google/YouTube device-flow login provider (platform-identity §3.2). One ILoginIdentityProvider
        // implementation per provider key; the generic auth/{provider}/device[/poll] routes dispatch to it.
        services.AddScoped<
            Application.Identity.Services.ILoginIdentityProvider,
            Identity.Login.GoogleYouTubeLoginProvider
        >();

        // The auth-code + PKCE login providers (platform-identity §10.3). One IAuthCodeLoginProvider
        // implementation per provider key; the generic auth/{provider}/authorize + /callback routes dispatch.
        services.AddScoped<
            Application.Identity.Services.IAuthCodeLoginProvider,
            Identity.Login.KickLoginProvider
        >();
        services.AddScoped<
            Application.Identity.Services.IAuthCodeLoginProvider,
            Identity.Login.TwitterLoginProvider
        >();

        // ISessionService, IScopeGrantService, IIntegrationOAuthService, IAuthService follow the
        // I<X>Service single-impl convention and are bound scoped by AddServicesByConvention above.

        // Caching — profile-selected (platform-conventions §3.10). Full/SaaS bind the Redis-backed distributed
        // cache + the cross-node rate-limiter store; lite binds the in-process memory cache + a per-instance
        // rate-limiter store. The IEventBus stays the in-process bus on every profile today (the cross-node
        // RedisEventBus is an additive adapter that lands with the SaaS conduit subsystem).
        string? redisConnectionString =
            configuration.GetConnectionString("Redis") ?? configuration["Redis:ConnectionString"];
        if (
            cacheProvider == CacheProviderKind.Redis
            && !string.IsNullOrWhiteSpace(redisConnectionString)
        )
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = "nomnomzbot:";
            });
            services.AddSingleton<ICacheService, DistributedCacheService>();

            // One shared multiplexer backs the distributed rate-limiter counter store.
            services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ =>
                StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnectionString)
            );
            services.AddSingleton<IRateLimiterPartitionStore, RedisRateLimiterPartitionStore>();
        }
        else
        {
            services.AddMemoryCache();
            services.AddDistributedMemoryCache();
            services.AddSingleton<ICacheService, MemoryCacheService>();
            services.AddSingleton<IRateLimiterPartitionStore, InMemoryRateLimiterPartitionStore>();
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
            Application.Economy.Services.IGameRandomizer,
            Economy.CsprngGameRandomizer
        >();

        // PipelineEngine (scoped — IPipelineEngine is not an I<X>Service, registered explicitly)
        services.AddScoped<IPipelineEngine, PipelineEngine>();
        // Save-time, fail-closed validator (broker-pattern invariant + type registry check).
        services.AddScoped<ICommandConfigValidator, CommandConfigValidator>();

        // Identity / tenant — HttpContext accessor + UnitOfWork are framework/infra, kept explicit.
        // The I<X>Service impls (ICurrentTenantService, IChannelAccessService, ICurrentUserService,
        // IAdminService, ICommandService, IChannelService, IRewardService, IWidgetService,
        // IUserService, IModerationService, IAuthService, IPermissionService, ITimerManagementService,
        // IMusicConfigService, ITtsConfigService, IEventResponseService, IPipelineService,
        // IFeatureService, IGdprService) are now discovered by AddServicesByConvention above.
        services.AddHttpContextAccessor();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Per-platform channel provisioning (cross-platform chat, item 6) — get-or-create the tenant Channel
        // for a streamer's YouTube/Kick presence. Not an I<X>Service, so registered explicitly.
        services.AddScoped<
            Application.Identity.Services.IPlatformChannelProvisioner,
            Identity.PlatformChannelProvisioner
        >();

        // Startup helpers consumed by concrete type (not pluggable markers) — kept explicit.
        services.AddTransient<StartupReadinessChecker>();
        services.AddScoped<IDatabaseMigrator, DatabaseMigrator>();
        services.AddScoped<SeedRunner>();
        services.AddScoped<SqliteMigrationService>();

        // Auto-moderation engine consumed by concrete type — kept explicit.
        services.AddScoped<AutoModerationEngine>();

        // Built-in commands — scoped because some implementations consume scoped services
        // (e.g. IMusicService). The catalog is also scoped so it receives a consistent
        // IEnumerable<IBuiltinCommand> from the DI container within each request scope.
        services.AddScoped<
            IBuiltinCommand,
            NomNomzBot.Infrastructure.Commands.Builtins.UptimeBuiltin
        >();
        services.AddScoped<
            IBuiltinCommand,
            NomNomzBot.Infrastructure.Commands.Builtins.SongRequestBuiltin
        >();
        services.AddScoped<
            IBuiltinCommand,
            NomNomzBot.Infrastructure.Commands.Builtins.SkipBuiltin
        >();
        services.AddScoped<
            IBuiltinCommand,
            NomNomzBot.Infrastructure.Commands.Builtins.QueueBuiltin
        >();
        services.AddScoped<
            IBuiltinCommand,
            NomNomzBot.Infrastructure.Commands.Builtins.VolumeBuiltin
        >();
        services.AddScoped<
            IBuiltinCommand,
            NomNomzBot.Infrastructure.Commands.Builtins.CurrentSongBuiltin
        >();
        services.AddScoped<
            IBuiltinCommand,
            NomNomzBot.Infrastructure.Commands.Builtins.CoinflipBuiltin
        >();
        services.AddScoped<
            IBuiltinCommand,
            NomNomzBot.Infrastructure.Commands.Builtins.DiceBuiltin
        >();
        services.AddScoped<
            IBuiltinCommand,
            NomNomzBot.Infrastructure.Commands.Builtins.SlotsBuiltin
        >();
        // Temporary-delegation chat surface (!permit/!unpermit) — permit:issue-gated in-command.
        services.AddScoped<
            IBuiltinCommand,
            NomNomzBot.Infrastructure.Identity.Builtins.PermitBuiltin
        >();
        services.AddScoped<
            IBuiltinCommand,
            NomNomzBot.Infrastructure.Identity.Builtins.UnpermitBuiltin
        >();
        services.AddScoped<IBuiltinCommandCatalog, BuiltinCommandCatalog>();
        // Per-channel enable/disable toggle management.
        services.AddScoped<IBuiltinCommandService, BuiltinCommandService>();

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

        // YouTube Data API v3 client backing the browser-source song-request provider's search/resolve
        // (music-sr.md §3.5.2). App-level YouTube:ApiKey — no per-user OAuth (music-sr.md decision #8).
        services.AddHttpClient("youtube");

        // YouTube live-chat READ transport (cross-platform combined chat, item 6). Stateless over the "youtube"
        // client + the broadcaster's youtube.readonly bearer, so a singleton the future poll worker can inject.
        services.AddSingleton<IYouTubeLiveChatClient, YouTubeLiveChatClient>();

        // The ONE custody path for the broadcaster's YouTube OAuth bearer (vault lookup + transparent
        // refresh) — shared by the music manage surface and the live-chat poller. Scoped: DbContext.
        services.AddScoped<IYouTubeAccessTokenProvider, YouTubeAccessTokenProvider>();

        // ── Discord (discord.md §7) — guild link, notification rules, dispatch + dedupe ──
        // IDiscordGuildService / IDiscordNotificationConfigService / IDiscordNotificationRoleService follow the
        // I<X>Service convention and are bound scoped by AddServicesByConvention above. The dispatcher + gateway
        // are not name-convention services, so they are registered explicitly (both scoped: DbContext + per-tenant
        // token resolution per call). The DiscordGoLiveNotificationHandler + SendDiscordNotificationAction are
        // auto-discovered (IEventHandler<> / ICommandAction scans). The 8 discord:* Gate-2 action keys are seeded
        // by ActionDefinitionSeeder.
        services.AddScoped<
            Application.Contracts.Discord.IDiscordNotificationDispatcher,
            Discord.DiscordNotificationDispatcher
        >();
        // Discord REST/gateway adapter — the only thing that talks to Discord. The named "discord" typed
        // HttpClient carries the resilience handler that honours Discord's 429 Retry-After (like Spotify/Twitch).
        services.AddHttpClient("discord").AddDiscordResilienceHandler();
        services.AddScoped<
            Application.Contracts.Discord.IDiscordBotGateway,
            Discord.Gateway.DiscordRestBotGateway
        >();
        // Interactions webhook — Ed25519 signature verifier over Discord:PublicKey (stateless → singleton;
        // reads config per call so a key change applies without restart). The interaction router
        // (IDiscordInteractionService) and the guild directory (IDiscordGuildDirectoryService) follow the
        // I<X>Service convention and are bound scoped by AddServicesByConvention above.
        services.AddSingleton<
            Application.Contracts.Discord.IDiscordInteractionVerifier,
            Discord.Interactions.DiscordInteractionVerifier
        >();

        // ChannelRegistry (singleton + hosted service — one instance serves IChannelRegistry
        // AND the hosted lifecycle, so it is wired explicitly and excluded from the worker scan).
        services.AddSingleton<IChannelRegistry, ChannelRegistry>();
        services.AddHostedService(sp => (ChannelRegistry)sp.GetRequiredService<IChannelRegistry>());
        // Populates the registry from DB on startup so commands and timers fire from the first message.
        services.AddHostedService<ChannelRegistryBootstrapService>();

        // BotLifecycleService, TimerService, and TokenRefreshService are auto-registered as
        // hosted services by AddHostedWorkers above. Content seeding is no longer a hosted
        // service — it runs once at startup via SeedRunner (backend-structure §5.1).

        // Twitch options
        services.Configure<TwitchOptions>(configuration.GetSection(TwitchOptions.SectionName));

        // Twitch HTTP clients with resilience
        services.AddHttpClient("twitch-auth");

        // Per-device-code poll rate limiter (singleton) — keeps the Device Code Flow poll within Twitch's
        // interval no matter how fast/many clients call us. Shared state, so it must outlive the scoped auth
        // services that consume it.
        services.AddSingleton<DeviceCodePollThrottle>();

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

        // Platform-bot readiness gate (scoped — rides the resolver's DbContext + vault). The single fact the
        // Twitch-dependent background work (EventSub transport, IRC, Helix warmers) checks so it stays dormant
        // on a fresh, un-onboarded install and activates the moment a bot account is authorized — no restart.
        services.AddScoped<IPlatformBotReadinessGate, PlatformBotReadinessGate>();

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
        services.AddScoped<ITwitchHelixClient, TwitchHelixClient>();

        // ITwitchAuthService → TwitchAuthService is a scoped single-impl service discovered by
        // AddServicesByConvention above. (The legacy ITwitchApiService has been retired — every caller now
        // targets the granular Helix sub-clients / ITwitchHelixClient façade.)

        // Roles & permissions — the effective-level resolver (Gate-2 reads it). Not an I<X>Service, so it is
        // registered explicitly rather than by AddServicesByConvention. Scoped (reads the per-request DbContext).
        services.AddScoped<
            Application.Contracts.Authorization.IRoleResolver,
            Identity.RoleResolver
        >();

        // Twitch management snapshot builder (moderators + editors) — shared by the onboarding seed and the
        // periodic ManagementRoleReconcileService. Not an I<X>Service; scoped (composes IUserService + Helix).
        services.AddScoped<
            Application.Contracts.Authorization.ITwitchManagementSnapshotBuilder,
            Identity.TwitchManagementSnapshotBuilder
        >();

        // Twitch identity resolver — the single seam translating tenant/user Guids ↔ Twitch string ids
        // (the invariant: Twitch never receives a Guid). Scoped: reads the per-request DbContext.
        services.AddScoped<ITwitchIdentityResolver, TwitchIdentityResolver>();

        // Chat platforms (BUILD slice 3 — the thin multi-platform seam): every send site keeps talking
        // to IChatProvider; the router selects the platform by the tenant channel's Channel.Provider.
        // Multi-bound platforms + the router are all scoped (DbContext + per-tenant token resolution).
        services.AddScoped<IChatPlatform, HelixChatProvider>();
        services.AddScoped<IChatPlatform, YouTubeChatPlatform>();
        services.AddScoped<IChatProvider, ChatPlatformRouter>();

        // The live YouTube chat session per YouTube tenant — written by the poll worker on go-live/
        // offline, read by the YouTube send path. Process-wide state, so a singleton.
        services.AddSingleton<IYouTubeLiveChatSessionRegistry, YouTubeLiveChatSessionRegistry>();

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
        services.AddSingleton<Application.Contracts.Platform.IEventSource>(sp =>
            sp.GetRequiredService<TwitchEventSubHostedService>()
        );
        services.AddHostedService(sp => sp.GetRequiredService<TwitchEventSubHostedService>());

        // ── Event store (event-store §7) ─────────────────────────────────────
        // Journal, allocator, subscriber, projection runner all touch the DbContext / IUnitOfWork (scoped).
        // The upcaster registry is pure over the singleton upcaster set (singleton). EventJournalRepository is
        // self-registered scoped by AddRepositoriesByConvention above (it derives from GenericRepository<T>).
        services.AddScoped<
            Application.Contracts.EventStore.ITenantSequenceAllocator,
            EventStore.TenantSequenceAllocator
        >();
        services.AddScoped<
            Application.Contracts.EventStore.IEventJournal,
            EventStore.EventJournalService
        >();
        services.AddScoped<
            Application.Contracts.EventStore.IEventStoreSubscriber,
            EventStore.EventStoreSubscriber
        >();
        services.AddScoped<
            Application.Contracts.EventStore.IProjectionRunner,
            EventStore.ProjectionRunner
        >();
        services.AddSingleton<
            Application.Contracts.EventStore.IEventUpcasterRegistry,
            EventStore.EventUpcasterRegistry
        >();
        // The projection driver (event-store.md §3.3) — periodically advances every projection to the journal
        // head so live appends reach the read models (without it, projections only move during import/rebuild).
        services.AddHostedService<EventStore.EventStoreProjectionDriver>();

        // Owner-gated legacy backfill — imports the legacy NoMercy bot's channel history onto the journal and
        // rebuilds projections from it. The locator (which legacy file to read) is a stateless seam; the service
        // composes the importer + projection runner.
        services.AddSingleton<
            EventStore.LegacyImport.ILegacyDatabaseLocator,
            EventStore.LegacyImport.DefaultLegacyDatabaseLocator
        >();
        services.AddScoped<Analytics.ChannelEventActorBackfill>();
        services.AddScoped<
            Application.Contracts.EventStore.ILegacyChannelImportService,
            EventStore.LegacyImport.LegacyChannelImportService
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
