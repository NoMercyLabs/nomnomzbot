// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Auth;
using NomNomzBot.Infrastructure.Platform.Configuration;
using NomNomzBot.Infrastructure.Platform.Persistence.Converters;
using NomNomzBot.Infrastructure.Platform.Persistence.Extensions;
using NomNomzBot.Infrastructure.Platform.Security;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Shared scaffolding for the auth behavior tests: a focused EF context over SQLite mapping only the
/// auth/integration entities (so it stays provider-agnostic where the production <c>AppDbContext</c>'s
/// jsonb-of-complex-type columns cannot materialize — the same reason it ran on the InMemory provider
/// before S004d moved it to a real relational engine for unique-index enforcement and
/// <c>ExecuteUpdateAsync</c> support), the REAL envelope-encryption crypto stack (so the vault round-trip
/// proves ciphertext-at-rest, not a stub), and a recording event bus.
/// </summary>
internal static class AuthTestBuilder
{
    // A fixed 32-byte base64 deployment key drives the deterministic KEK fallback (no OS keystore needed).
    private const string ConfigKey = "Zm9yLXRlc3Qtb25seS1rZWstMzItYnl0ZXMtbG9uZyEh";

    /// <summary>
    /// Builds the real token protector over the real envelope crypto stack, backed by the persisted DEK store
    /// (<see cref="CryptoKeySubjectKeyStore"/>) over <paramref name="db"/>. Passing the same context the vault uses
    /// keeps the DEK registry and the ciphertext that references it in one store — exactly the production wiring.
    /// </summary>
    public static ITokenProtector RealTokenProtector(
        IApplicationDbContext db,
        out ISubjectKeyService subjectKeys
    )
    {
        IFieldCipher cipher = new AesGcmFieldCipher();
        IKeyVault vault = new OsSecureStoreKeyVault(
            Options.Create(new EncryptionOptions { Key = ConfigKey }),
            NullLogger<OsSecureStoreKeyVault>.Instance
        );
        ISubjectKeyStore store = new CryptoKeySubjectKeyStore(db);
        subjectKeys = new SubjectKeyService(
            vault,
            cipher,
            store,
            TimeProvider.System,
            NullLogger<SubjectKeyService>.Instance
        );
        return new TokenProtector(subjectKeys, NullLogger<TokenProtector>.Instance);
    }

    /// <summary>
    /// A real <see cref="UserService"/> wired over the same context + scope factory the caller uses, with its
    /// platform-agnostic <see cref="UserIdentityService"/> collaborator (recording bus, system clock). Every
    /// get-or-create seam thus routes through the identity resolver exactly as production does.
    /// </summary>
    public static IUserService UserService(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        IServiceScopeFactory scopeFactory
    ) =>
        new UserService(
            db,
            currentUser,
            scopeFactory,
            new UserIdentityService(db, scopeFactory, TimeProvider.System, new RecordingEventBus()),
            TimeProvider.System
        );

    // One keep-alive connection per shared database name — a SQLite "cache=shared" in-memory database
    // is torn down the instant its last open connection closes, so this holds one open for the process's
    // lifetime while every AuthDbContext opens its OWN connection to the same URI (real SQLite semantics —
    // unique-index enforcement, ExecuteUpdateAsync support — unlike the InMemory provider this harness used
    // to run on, which could not run ExecuteUpdateAsync and enforced no unique index at all).
    private static readonly ConcurrentDictionary<string, SqliteConnection> KeepAliveConnections =
        new();

    // Guards schema creation for a brand-new shared-cache database: a concurrency test that opens several
    // contexts against the SAME new name in parallel would otherwise race EnsureCreated() against itself
    // (every context sees "no schema yet" and tries to CREATE TABLE, and every one after the first fails
    // with "table already exists").
    private static readonly object SchemaCreationLock = new();

    public static AuthDbContext NewContext() => NewContext(Guid.NewGuid().ToString());

    /// <summary>
    /// A context over a named in-memory SQLite store. Two contexts built with the SAME name share one backing
    /// store — the test analogue of a process restart against the same persisted database.
    /// </summary>
    public static AuthDbContext NewContext(string databaseName)
    {
        KeepAliveConnections.GetOrAdd(
            databaseName,
            static name =>
            {
                SqliteConnection connection = new(SharedCacheConnectionString(name));
                connection.Open();
                return connection;
            }
        );
        AuthDbContext db = new(
            new DbContextOptionsBuilder<AuthDbContext>()
                .UseSqlite(SharedCacheConnectionString(databaseName))
                .Options
        );
        // Microsoft.Data.Sqlite turns FK enforcement ON for every connection it opens. This harness is
        // deliberately a FOCUSED, scalar-only mapping (see the class doc) — most navigations are `.Ignore`d
        // on purpose so a test can seed e.g. a UserIdentity without its owning User, exactly the partial
        // graphs the InMemory provider used to tolerate silently. Enforcing FK integrity here would fight
        // that design intent rather than serve it, so it stays off; the properties this migration actually
        // needs from a relational engine — unique-index enforcement and ExecuteUpdateAsync — are unaffected.
        db.Database.OpenConnection();
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
        lock (SchemaCreationLock)
        {
            db.Database.EnsureCreated();
        }
        return db;
    }

    private static string SharedCacheConnectionString(string databaseName) =>
        $"Data Source=file:{databaseName}?mode=memory&cache=shared";

    /// <summary>
    /// Parks the keep-alive connection and creates the schema for <paramref name="databaseName"/>, then hands
    /// back the connection string — for tests that register <see cref="AuthDbContext"/> in a DI container
    /// (<c>AddDbContext(o =&gt; o.UseSqlite(...))</c>) instead of building one through
    /// <see cref="NewContext(string)"/>. Same real relational store either way.
    /// </summary>
    public static string SharedDatabase(string databaseName)
    {
        NewContext(databaseName).Dispose();
        return SharedCacheConnectionString(databaseName);
    }

    /// <summary>
    /// A real <see cref="ISystemCredentialsProvider"/> over the test context + REAL token protector, so a
    /// test proves the DB-vaulted-first → config-fallback resolution and the AAD binding for real (no stub).
    /// Builds a minimal <see cref="ServiceCollection"/> to supply an <see cref="IServiceScopeFactory"/> that
    /// hands back the same <paramref name="db"/> + <paramref name="protector"/> instances from inner scopes —
    /// matching the production wiring without needing a full DI host.
    /// </summary>
    public static ISystemCredentialsProvider CredentialsProvider(
        AuthDbContext db,
        ITokenProtector protector,
        IConfiguration configuration
    )
    {
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(protector);
        ServiceProvider sp = services.BuildServiceProvider();
        return new SystemCredentialsProvider(
            sp.GetRequiredService<IServiceScopeFactory>(),
            configuration
        );
    }

    /// <summary>
    /// A real <see cref="IChannelCredentialsResolver"/> over the test context + REAL token protector, layered
    /// over the given <paramref name="systemCredentials"/> — so a test proves the channel-own-wins →
    /// app-level-fallback → not-configured resolution for real (no stub).
    /// </summary>
    public static IChannelCredentialsResolver ChannelCredentialsResolver(
        AuthDbContext db,
        ITokenProtector protector,
        ISystemCredentialsProvider systemCredentials
    )
    {
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(protector);
        ServiceProvider sp = services.BuildServiceProvider();
        return new ChannelCredentialsResolver(
            sp.GetRequiredService<IServiceScopeFactory>(),
            systemCredentials
        );
    }
}

/// <summary>Records every published domain event so a test can assert the side effect actually fired.</summary>
internal sealed class RecordingEventBus : IEventBus
{
    public List<IDomainEvent> Published { get; } = [];

    /// <summary>
    /// Opt-in: make every publish throw, so a test can prove what an ingest does when its DISPATCH fails
    /// rather than only when it succeeds. Default off — every existing user is unaffected.
    /// </summary>
    public bool ThrowOnPublish { get; init; }

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IDomainEvent
    {
        if (ThrowOnPublish)
            throw new InvalidOperationException("publish failed (test double)");
        Published.Add(@event);
        return Task.CompletedTask;
    }

    public void PublishFireAndForget<TEvent>(TEvent @event)
        where TEvent : class, IDomainEvent
    {
        if (ThrowOnPublish)
            throw new InvalidOperationException("publish failed (test double)");
        Published.Add(@event);
    }
}

/// <summary>
/// A deterministic, controllable <see cref="IRunOnceGuard"/> test double simulating one shared
/// lease store (the "one database") backing two API instances. Two instances of this type built
/// over the SAME <paramref name="backingStore"/> mimic two overlapping processes racing the same
/// named resource: whichever calls <see cref="TryAcquireAsync"/> first holds the lease until its
/// lease is disposed; the other is refused (returns <c>null</c>) for as long as the first holds it —
/// giving a test full control over which instance "wins" a given tick without depending on real
/// async timing.
/// </summary>
internal sealed class SharedFakeRunOnceGuard(ConcurrentDictionary<string, byte> backingStore)
    : IRunOnceGuard
{
    public SharedFakeRunOnceGuard()
        : this(new ConcurrentDictionary<string, byte>()) { }

    public Task<IAsyncDisposable?> TryAcquireAsync(
        string resourceName,
        TimeSpan ttl,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult<IAsyncDisposable?>(
            backingStore.TryAdd(resourceName, 0) ? new Lease(backingStore, resourceName) : null
        );

    private sealed class Lease(ConcurrentDictionary<string, byte> backingStore, string resourceName)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            backingStore.TryRemove(resourceName, out _);
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// Focused EF context over the auth/integration entities. Maps only what the services under test touch;
/// every other <see cref="IApplicationDbContext"/> member throws, since the tests never reach them.
/// </summary>
internal sealed class AuthDbContext : DbContext, IApplicationDbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options)
        : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserIdentity> UserIdentities => Set<UserIdentity>();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.Redemption> Redemptions =>
        Set<NomNomzBot.Domain.Rewards.Entities.Redemption>();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.RedemptionTimer> RedemptionTimers =>
        Set<NomNomzBot.Domain.Rewards.Entities.RedemptionTimer>();
    public DbSet<NomNomzBot.Domain.Commands.Entities.ChatTrigger> ChatTriggers =>
        Set<NomNomzBot.Domain.Commands.Entities.ChatTrigger>();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.ChannelModerationStanding> ChannelModerationStandings =>
        Set<NomNomzBot.Domain.Moderation.Entities.ChannelModerationStanding>();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.SharedBanSettings> SharedBanSettings =>
        Set<NomNomzBot.Domain.Moderation.Entities.SharedBanSettings>();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.SharedBanTrustedChannel> SharedBanTrustedChannels =>
        Set<NomNomzBot.Domain.Moderation.Entities.SharedBanTrustedChannel>();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.NetworkNukeBatch> NetworkNukeBatches =>
        Set<NomNomzBot.Domain.Moderation.Entities.NetworkNukeBatch>();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.UserModerationHistory> UserModerationHistories =>
        Set<NomNomzBot.Domain.Moderation.Entities.UserModerationHistory>();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.UserTrustScore> UserTrustScores =>
        Set<NomNomzBot.Domain.Moderation.Entities.UserTrustScore>();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.ModerationEscalationPolicy> ModerationEscalationPolicies =>
        Set<NomNomzBot.Domain.Moderation.Entities.ModerationEscalationPolicy>();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.ModerationEscalationState> ModerationEscalationStates =>
        Set<NomNomzBot.Domain.Moderation.Entities.ModerationEscalationState>();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.ChatFilter> ChatFilters =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.ModerationQueueItem> ModerationQueueItems =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Notifications.Entities.ActionRequiredDismissal> ActionRequiredDismissals =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Trust.Entities.TrustPolicy> TrustPolicies =>
        throw new NotSupportedException();

    public DbSet<NomNomzBot.Domain.Moderation.Entities.SpamDefensePolicy> SpamDefensePolicies =>
        throw new NotSupportedException();

    public DbSet<NomNomzBot.Domain.Moderation.Entities.SpamDetection> SpamDetections =>
        throw new NotSupportedException();

    public DbSet<NomNomzBot.Domain.Moderation.Entities.SpamCampaignRecord> SpamCampaigns =>
        throw new NotSupportedException();

    public DbSet<NomNomzBot.Domain.Moderation.Entities.FollowBotBlock> FollowBotBlocks =>
        throw new NotSupportedException();

    public DbSet<NomNomzBot.Domain.Moderation.Entities.SpamSignature> SpamSignatures =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Community.Entities.ChatPoll> ChatPolls =>
        Set<NomNomzBot.Domain.Community.Entities.ChatPoll>();
    public DbSet<NomNomzBot.Domain.Community.Entities.ChatPollVote> ChatPollVotes =>
        Set<NomNomzBot.Domain.Community.Entities.ChatPollVote>();
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
    public DbSet<ErasureRequest> ErasureRequests => Set<ErasureRequest>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<PlatformConnection> PlatformConnections => Set<PlatformConnection>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<IntegrationConnection> IntegrationConnections => Set<IntegrationConnection>();
    public DbSet<IntegrationToken> IntegrationTokens => Set<IntegrationToken>();
    public DbSet<CryptoKey> CryptoKeys => Set<CryptoKey>();
    public DbSet<KeyUsageBinding> KeyUsageBindings => Set<KeyUsageBinding>();
    public DbSet<NomNomzBot.Domain.EventStore.Entities.EventSubjectKey> EventSubjectKeys =>
        Set<NomNomzBot.Domain.EventStore.Entities.EventSubjectKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasKey(e => e.Id);
        b.Entity<User>().Ignore(e => e.Channel).Ignore(e => e.Pronoun);

        // Widget: mapped scalar-only, mirroring WidgetTestDbContext's own Widget config exactly.
        b.Entity<NomNomzBot.Domain.Widgets.Entities.Widget>(e =>
        {
            e.HasKey(w => w.Id);
            e.Ignore(w => w.Channel);
            e.Property(w => w.EventSubscriptions)
                .HasConversion(
                    JsonValueConverter.Converter<List<string>>(),
                    JsonValueConverter.Comparer<List<string>>()
                );
            e.Property(w => w.Settings)
                .HasConversion(
                    JsonValueConverter.Converter<Dictionary<string, object>>(),
                    JsonValueConverter.Comparer<Dictionary<string, object>>()
                );
        });

        // Scalar-only mapping (navs ignored) so the platform-identity tests resolve + list identities here.
        b.Entity<UserIdentity>().HasKey(e => e.Id);
        b.Entity<UserIdentity>().Ignore(e => e.User).Ignore(e => e.Connection);

        b.Entity<Channel>().HasKey(e => e.Id);
        b.Entity<Channel>().Ignore(e => e.Tags).Ignore(e => e.ContentLabels);
        b.Entity<Channel>().Ignore(e => e.Moderators).Ignore(e => e.Streams).Ignore(e => e.Events);

        // Channel.User is mapped (via the domain entity's own [ForeignKey(nameof(OwnerUserId))]) so
        // ChannelService tests can exercise its `.Include(c => c.User)` reads through this harness.

        b.Entity<AuthSession>().HasKey(e => e.Id);
        b.Entity<AuthSession>()
            .Ignore(e => e.User)
            .Ignore(e => e.Channel)
            .Ignore(e => e.RefreshTokens);

        b.Entity<RefreshToken>().HasKey(e => e.Id);
        b.Entity<RefreshToken>().Ignore(e => e.Session).Ignore(e => e.User);

        b.Entity<IntegrationConnection>().HasKey(e => e.Id);
        b.Entity<IntegrationConnection>().Ignore(e => e.Channel).Ignore(e => e.Tokens);
        // Mirrors IntegrationConnectionConfiguration's DB-enforced half of the vault's upsert: one LIVE
        // connection per (BroadcasterId, Provider). The InMemory provider never enforced this — this harness
        // moved to real SQLite (S004d) exactly so a duplicate-insert race is a real UNIQUE violation here too.
        b.Entity<IntegrationConnection>()
            .HasIndex(e => new { e.BroadcasterId, e.Provider })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");

        b.Entity<IntegrationToken>().HasKey(e => e.Id);
        b.Entity<IntegrationToken>().Ignore(e => e.Connection).Ignore(e => e.Channel);

        // The persisted DEK registry (scalar-only) — mapped so the vault/protector tests seal and re-open tokens
        // through the same store the production wiring uses, and so the restart-survival test can prove it.
        b.Entity<CryptoKey>().HasKey(e => e.Id);

        // Reactive missing-scope rows (scalar; nav ignored) — mapped so the ScopeNotificationService tests seed
        // + query gaps through this harness.
        b.Entity<ChannelMissingScope>().HasKey(e => e.Id);
        b.Entity<ChannelMissingScope>().Ignore(e => e.Channel);

        // System-config table (scalar Key/Value/SecureValue) — mapped so the system-credentials provider
        // tests can seed wizard-vaulted rows and prove the DB-first resolution + AAD binding.
        b.Entity<NomNomzBot.Domain.Platform.Entities.Configuration>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Platform.Entities.Configuration>().Ignore(e => e.Channel);

        // Global pronoun catalog (scalar-only) — mapped so SetPronounAction tests can seed a catalog entry.
        b.Entity<Pronoun>().HasKey(e => e.Id);

        // At-most-once webhook/event redelivery marker (scalar-only, no navs) — mapped so KickWebhookIngest's
        // redelivery-dedupe tests can prove a repeated follow/sub/gift/ban/redemption is processed once.
        // The REAL configuration is applied, not a bare HasKey: the dedupe is an atomic insert that relies on
        // the unique (Scope, Key, BroadcasterId) index to reject the loser of a race. With only a primary key
        // mapped, the second insert would succeed and a concurrency test would pass while proving nothing.
        b.ApplyConfiguration(
            new NomNomzBot.Infrastructure.Platform.Persistence.Configurations.IdempotencyKeyConfiguration()
        );

        // Mapped standalone (navs ignored, Channel.Moderators already ignored above) so the
        // ChannelAccessService tests can exercise the moderator-grant branch of tenant resolution.
        b.Entity<ChannelModerator>().HasKey(e => new { e.ChannelId, e.UserId });
        b.Entity<ChannelModerator>().Ignore(e => e.Channel).Ignore(e => e.User);

        // Service is scalar-only (the Scopes string[] materializes on InMemory), so it maps cleanly (nav ignored)
        // — mapped so the IntegrationOAuthService mirror test can prove the connect flow writes the legacy
        // `Service` token row the music providers read.
        b.Entity<NomNomzBot.Domain.Platform.Entities.Service>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Platform.Entities.Service>().Ignore(e => e.Channel);

        // DiscordGuildConnection: mapped scalar-only (nav ignored) so IntegrationStatusService tests can
        // exercise its "any non-deleted guild connection" existence check through this harness.
        b.Entity<NomNomzBot.Domain.Discord.Entities.DiscordGuildConnection>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Discord.Entities.DiscordGuildConnection>()
            .Ignore(e => e.Channel);

        // EF discovers entity types from the DbSet<T> property declarations regardless of the throwing
        // getter bodies, then tries to map their jsonb-of-complex-type columns (unsupported on InMemory).
        // Ignore every entity these tests do not exercise so the model stays minimal and provider-agnostic.
        // Widget itself is now explicitly mapped above (the marketplace bundle-import round-trip tests need
        // it); only its still-unexercised siblings stay ignored.
        b.Ignore<NomNomzBot.Domain.Widgets.Entities.WidgetVersion>();
        b.Ignore<NomNomzBot.Domain.Widgets.Entities.WidgetGalleryItem>();
        b.Ignore<NomNomzBot.Domain.Widgets.Entities.WidgetGallerySubmissionEvent>();

        // Command: mapped scalar + its Pipeline nav (Aliases/TemplateResponses are primitive collections
        // that materialize on InMemory) so CommandUseCountHandler tests can prove the UseCount/LastUsedAt
        // fold through this harness, and so ChannelRegistry's command-cache load (which joins Pipeline for
        // GraphJsonCache/IsEnabled) runs cleanly through this harness too.
        b.Entity<NomNomzBot.Domain.Commands.Entities.Command>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Commands.Entities.Command>().Ignore(e => e.Channel);

        // Reward is scalar-only (no jsonb-of-complex-type column), so it materializes on InMemory. Mapped
        // (navs ignored) so the reward-sync tests can prove the Twitch read path through this harness.
        b.Entity<NomNomzBot.Domain.Rewards.Entities.Reward>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Rewards.Entities.Reward>().Ignore(e => e.Channel);

        // SoundClip: mapped scalar-only (Channel/CreatedByUser navs ignored) so ChannelRegistry's
        // sound-trigger cache load (LoadSoundTriggersAsync) runs cleanly through this harness — reached
        // by the channel-registry bootstrap run-once tests, which drive the real GetOrCreateAsync path.
        b.Entity<NomNomzBot.Domain.Sound.Entities.SoundClip>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Sound.Entities.SoundClip>()
            .Ignore(e => e.Channel)
            .Ignore(e => e.CreatedByUser);
        b.Ignore<NomNomzBot.Domain.Platform.Entities.EventSubSubscription>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.EventSubConduit>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.EventSubConduitShard>();

        // Stream / ChannelEvent / CommandUsage: mapped scalar-only (navs + primitive collections
        // ignored) so the per-stream analytics tests can seed a stream window and prove the folds.
        b.Entity<NomNomzBot.Domain.Stream.Entities.Stream>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Stream.Entities.Stream>()
            .Ignore(e => e.Channel)
            .Ignore(e => e.Tags)
            .Ignore(e => e.ContentLabels);
        // StartedAt/EndedAt DateTimeOffset comparison/ORDER BY translation on SQLite is handled model-wide
        // by ApplySqliteCompatibility (see the call at the end of this method) — S004e made the per-entity
        // UTC-ticks conversion a model-level concern, closing the drift risk of hand-rolling it per column.
        b.Entity<ChannelEvent>().HasKey(e => e.Id);
        b.Entity<ChannelEvent>().Ignore(e => e.Channel).Ignore(e => e.User);
        b.Entity<NomNomzBot.Domain.Commands.Entities.CommandUsage>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Commands.Entities.CommandUsage>().Ignore(e => e.Command);

        // CustomDataSource: mapped scalar-only (navs ignored; FieldMapJson/EndpointUrl/AuthSecretCipher are
        // plain string columns that materialize on InMemory) so the poll-ingress fetcher tests can seed sources
        // + the H.7 allowlist and prove the SSRF egress gate through this harness.
        b.Entity<NomNomzBot.Domain.CustomEvents.Entities.CustomDataSource>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.CustomEvents.Entities.CustomDataSource>()
            .Ignore(e => e.Channel)
            .Ignore(e => e.CreatedByUser)
            .Ignore(e => e.InboundWebhookEndpoint);

        // ChatMessage: mapped scalar-only (navs + jsonb fragment/badge collections ignored) so the
        // YouTube live-chat poll worker tests can prove the persisted-message dedupe through this harness.
        b.Entity<NomNomzBot.Domain.Chat.Entities.ChatMessage>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Chat.Entities.ChatMessage>()
            .Ignore(e => e.Channel)
            .Ignore(e => e.Stream)
            .Ignore(e => e.Fragments)
            .Ignore(e => e.Badges);
        // Script KV storage (scalar Key/Value; nav ignored) — mapped so the ScriptStorageService / host-bridge
        // storage tests can prove persistence, caps, and tenant isolation through this harness.
        b.Entity<NomNomzBot.Domain.Platform.Entities.Storage>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Platform.Entities.Storage>().Ignore(e => e.Channel);
        b.Ignore<NomNomzBot.Domain.Platform.Entities.Record>();
        b.Ignore<Permission>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.ChannelFeature>();
        // ChannelBotAuthorization: mapped scalar-only (Channel/BotAccount navs ignored) so
        // ChatPlatformRouter's bot-line-prefix dedicated-bot lookup (S011) can seed/query it through this
        // harness.
        b.Entity<ChannelBotAuthorization>().HasKey(e => e.Id);
        b.Entity<ChannelBotAuthorization>().Ignore(e => e.Channel).Ignore(e => e.BotAccount);

        // IpcDevModeKey: mapped scalar-only (CreatedByUser nav ignored) so the IpcDevModeService tests
        // can prove hash-only storage, tombstoning, and constant-time auth through this harness.
        b.Entity<IpcDevModeKey>().HasKey(e => e.Id);
        b.Entity<IpcDevModeKey>().Ignore(e => e.CreatedByUser);

        // BotAccount is scalar-only (no navigation properties at all), so it materializes on InMemory as-is.
        // Mapped so BotJoinOnOnboardingHandler tests can seed/query the shared platform bot through this harness.
        b.Entity<BotAccount>().HasKey(e => e.Id);
        // DiscordGuildConnection is mapped above (not ignored here) so IntegrationStatusService tests
        // can exercise its Discord existence check through this harness.
        b.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordNotificationConfig>();
        b.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordNotificationRole>();
        b.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordMemberOptIn>();
        b.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordNotificationDispatch>();
        b.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordLiveRoleConfig>();
        b.Ignore<ChannelSubscription>();
        b.Ignore<NomNomzBot.Domain.Tts.Entities.TtsVoice>();
        b.Ignore<NomNomzBot.Domain.Tts.Entities.UserTtsVoice>();
        b.Ignore<NomNomzBot.Domain.Tts.Entities.TtsUsageRecord>();
        b.Ignore<NomNomzBot.Domain.Tts.Entities.TtsCacheEntry>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.DeletionAuditLog>();

        // Timer: mapped scalar-only (navs ignored; Messages is a primitive collection that materializes
        // on InMemory) so TimerService tests can drive the pipeline-dispatch + rotation fold through this
        // harness.
        b.Entity<NomNomzBot.Domain.Commands.Entities.Timer>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Commands.Entities.Timer>()
            .Ignore(e => e.Channel)
            .Ignore(e => e.Pipeline);
        b.Ignore<NomNomzBot.Domain.Rewards.Entities.WatchStreak>();

        // EventResponse: mapped scalar-only (MetadataJson's jsonb column and both navs ignored) so
        // EventResponseSeedOnOnboardingHandler tests can seed/query the six default responses through this
        // harness.
        b.Entity<NomNomzBot.Domain.Commands.Entities.EventResponse>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Commands.Entities.EventResponse>()
            .Ignore(e => e.Channel)
            .Ignore(e => e.Pipeline)
            .Ignore(e => e.MetadataJson);

        // ChannelBuiltinCommand: mapped scalar-only (nav ignored) so DefaultCommandsSeedOnOnboardingHandler
        // tests can drive the real DefaultCommandsSeeder through this harness.
        b.Entity<NomNomzBot.Domain.Commands.Entities.ChannelBuiltinCommand>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Commands.Entities.ChannelBuiltinCommand>()
            .Ignore(e => e.Channel);

        // Pipeline: mapped scalar-only (Steps + Channel navs ignored) so timer→pipeline dispatch tests
        // can seed a GraphJsonCache through this harness.
        b.Entity<NomNomzBot.Domain.Commands.Entities.Pipeline>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Commands.Entities.Pipeline>()
            .Ignore(e => e.Channel)
            .Ignore(e => e.Steps);
        // DueAt/CreatedAt/FiredAt are DateTimeOffset — comparison/ORDER BY translation on SQLite (the
        // sweeper's `t.DueAt <= now`) is handled model-wide by ApplySqliteCompatibility below.
        b.Entity<NomNomzBot.Domain.Commands.Entities.ScheduledPipelineTask>().HasKey(e => e.Id);

        // PipelineStep/PipelineStepCondition: mapped scalar + the Step->Conditions relation (Pipeline
        // nav ignored) so PipelineService's normalized-row dual-write (S-PIPE-WRITE-SYMMETRY) round-trips
        // through this harness the same way it does against the real AppDbContext.
        b.Entity<NomNomzBot.Domain.Commands.Entities.PipelineStep>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Commands.Entities.PipelineStep>().Ignore(e => e.Pipeline);
        b.Entity<NomNomzBot.Domain.Commands.Entities.PipelineStep>()
            .HasMany(e => e.Conditions)
            .WithOne(c => c.Step)
            .HasForeignKey(c => c.PipelineStepId);
        b.Entity<NomNomzBot.Domain.Commands.Entities.PipelineStepCondition>().HasKey(e => e.Id);
        b.Ignore<NomNomzBot.Domain.EventStore.Entities.EventJournal>();
        b.Ignore<NomNomzBot.Domain.EventStore.Entities.TenantSequence>();
        b.Ignore<NomNomzBot.Domain.EventStore.Entities.ProjectionCheckpoint>();

        // Mirrors ChannelAnalyticsDailyConfiguration / ChannelChatterDayConfiguration: one upserted row per
        // (channel, day) / (channel, day, viewer-hash). The InMemory provider never enforced these, which is
        // exactly why ChannelAnalyticsDailyProjection's insert-race handling (S004d) was unverifiable before
        // this harness moved to a real relational engine.
        b.Entity<NomNomzBot.Domain.Analytics.Entities.ChannelAnalyticsDaily>()
            .HasIndex(e => new { e.BroadcasterId, e.ActivityDate })
            .IsUnique();
        b.Entity<NomNomzBot.Domain.Analytics.Entities.ChannelChatterDay>()
            .HasIndex(e => new
            {
                e.BroadcasterId,
                e.ActivityDate,
                e.ChatterHash,
            })
            .IsUnique();

        // Mirrors WatchSessionConfiguration (S004f): one open/derived session per (channel, viewer,
        // stream) — DB-enforced so WatchSessionConcurrencyTests can prove GetOrOpenAsync's insert race
        // converges on a single row instead of relying on unenforced application logic alone.
        b.Entity<NomNomzBot.Domain.Analytics.Entities.WatchSession>()
            .HasIndex(e => new
            {
                e.BroadcasterId,
                e.ViewerUserId,
                e.StreamId,
            })
            .IsUnique();

        // Model-wide DateTimeOffset → UTC-ticks conversion (S004e) — replaces the per-entity converters
        // this harness used to hand-roll on Stream.StartedAt/EndedAt and ScheduledPipelineTask's
        // DueAt/CreatedAt/FiredAt columns, so SQLite can translate ORDER BY / relational comparisons on
        // every DateTimeOffset column without drifting one call-site at a time.
        b.ApplySqliteCompatibility();
    }

    // ── Unused IApplicationDbContext surface — never reached by these tests ──
    public DbSet<ChannelModerator> ChannelModerators => Set<ChannelModerator>();
    public DbSet<NomNomzBot.Domain.Platform.Entities.Service> Services =>
        Set<NomNomzBot.Domain.Platform.Entities.Service>();
    public DbSet<NomNomzBot.Domain.Commands.Entities.Command> Commands =>
        Set<NomNomzBot.Domain.Commands.Entities.Command>();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.Reward> Rewards =>
        Set<NomNomzBot.Domain.Rewards.Entities.Reward>();
    public DbSet<NomNomzBot.Domain.Quotes.Entities.Quote> Quotes =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Music.Entities.BlockedTrack> BlockedTracks =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.PickLists.Entities.PickList> PickLists =>
        throw new NotSupportedException();

    // Widget: mapped scalar-only (Channel nav ignored, Settings/EventSubscriptions JSON-converted — the
    // same shape WidgetTestDbContext uses) so the marketplace bundle-import round-trip tests can drive the
    // real BundleImportService's widget-conflict check through this harness.
    public DbSet<NomNomzBot.Domain.Widgets.Entities.Widget> Widgets =>
        Set<NomNomzBot.Domain.Widgets.Entities.Widget>();
    public DbSet<NomNomzBot.Domain.Widgets.Entities.WidgetVersion> WidgetVersions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Widgets.Entities.WidgetGalleryItem> WidgetGalleryItems =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Widgets.Entities.WidgetGallerySubmissionEvent> WidgetGallerySubmissionEvents =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Widgets.Entities.RenderedAlertCapture> RenderedAlertCaptures =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.EventSubSubscription> EventSubSubscriptions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.EventSubConduit> EventSubConduits =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.EventSubConduitShard> EventSubConduitShards =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.IdempotencyKey> IdempotencyKeys =>
        Set<NomNomzBot.Domain.Platform.Entities.IdempotencyKey>();
    public DbSet<NomNomzBot.Domain.Chat.Entities.ChatMessage> ChatMessages =>
        Set<NomNomzBot.Domain.Chat.Entities.ChatMessage>();
    public DbSet<NomNomzBot.Domain.Chat.Entities.YouTubeLiveChatBan> YouTubeLiveChatBans =>
        Set<NomNomzBot.Domain.Chat.Entities.YouTubeLiveChatBan>();
    public DbSet<NomNomzBot.Domain.Giveaways.Entities.Giveaway> Giveaways =>
        Set<NomNomzBot.Domain.Giveaways.Entities.Giveaway>();
    public DbSet<NomNomzBot.Domain.Giveaways.Entities.GiveawayEntry> GiveawayEntries =>
        Set<NomNomzBot.Domain.Giveaways.Entities.GiveawayEntry>();
    public DbSet<NomNomzBot.Domain.Giveaways.Entities.GiveawayWinner> GiveawayWinners =>
        Set<NomNomzBot.Domain.Giveaways.Entities.GiveawayWinner>();
    public DbSet<NomNomzBot.Domain.Giveaways.Entities.GiveawayCodePool> GiveawayCodePools =>
        Set<NomNomzBot.Domain.Giveaways.Entities.GiveawayCodePool>();
    public DbSet<NomNomzBot.Domain.Giveaways.Entities.GiveawayCode> GiveawayCodes =>
        Set<NomNomzBot.Domain.Giveaways.Entities.GiveawayCode>();
    public DbSet<ChannelEvent> ChannelEvents => Set<ChannelEvent>();
    public DbSet<NomNomzBot.Domain.Stream.Entities.Stream> Streams =>
        Set<NomNomzBot.Domain.Stream.Entities.Stream>();
    public DbSet<NomNomzBot.Domain.Platform.Entities.Configuration> Configurations =>
        Set<NomNomzBot.Domain.Platform.Entities.Configuration>();
    public DbSet<NomNomzBot.Domain.Platform.Entities.Storage> Storages =>
        Set<NomNomzBot.Domain.Platform.Entities.Storage>();
    public DbSet<NomNomzBot.Domain.Platform.Entities.Record> Records =>
        throw new NotSupportedException();
    public DbSet<Permission> Permissions => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.ChannelFeature> ChannelFeatures =>
        throw new NotSupportedException();
    public DbSet<ChannelBotAuthorization> ChannelBotAuthorizations =>
        Set<ChannelBotAuthorization>();
    public DbSet<BotAccount> BotAccounts => Set<BotAccount>();
    public DbSet<IpcDevModeKey> IpcDevModeKeys => Set<IpcDevModeKey>();
    public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordGuildConnection> DiscordGuildConnections =>
        Set<NomNomzBot.Domain.Discord.Entities.DiscordGuildConnection>();
    public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordNotificationConfig> DiscordNotificationConfigs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordNotificationRole> DiscordNotificationRoles =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordMemberOptIn> DiscordMemberOptIns =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordNotificationDispatch> DiscordNotificationDispatches =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordLiveRoleConfig> DiscordLiveRoleConfigs =>
        throw new NotSupportedException();
    public DbSet<ChannelSubscription> ChannelSubscriptions => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Vts.Entities.VtsConnection> VtsConnections =>
        Set<NomNomzBot.Domain.Vts.Entities.VtsConnection>();
    public DbSet<NomNomzBot.Domain.Obs.Entities.ObsConnection> ObsConnections =>
        Set<NomNomzBot.Domain.Obs.Entities.ObsConnection>();
    public DbSet<Domain.Automation.Entities.AutomationApiToken> AutomationApiTokens =>
        Set<Domain.Automation.Entities.AutomationApiToken>();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsConfig> TtsConfigs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsVoice> TtsVoices =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.UserTtsVoice> UserTtsVoices =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsUsageRecord> TtsUsageRecords =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsCacheEntry> TtsCacheEntries =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsLexiconEntry> TtsLexiconEntries =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsApprovalQueueEntry> TtsApprovalQueueEntries =>
        throw new NotSupportedException();
    public DbSet<Pronoun> Pronouns => Set<Pronoun>();
    public DbSet<NomNomzBot.Domain.Platform.Entities.DeletionAuditLog> DeletionAuditLogs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Stream.Entities.ShoutoutOverride> ShoutoutOverrides =>
        Set<NomNomzBot.Domain.Stream.Entities.ShoutoutOverride>();
    public DbSet<ComplianceAuditLog> ComplianceAuditLogs => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.Timer> Timers =>
        Set<NomNomzBot.Domain.Commands.Entities.Timer>();
    public DbSet<NomNomzBot.Domain.Commands.Entities.EventResponse> EventResponses =>
        Set<NomNomzBot.Domain.Commands.Entities.EventResponse>();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.WatchStreak> WatchStreaks =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.Pipeline> Pipelines =>
        Set<NomNomzBot.Domain.Commands.Entities.Pipeline>();

    // ScheduledPipelineTask: scalar-only (VariablesJson is a plain string), so it materializes on InMemory —
    // mapped live so the deferred-pipeline scheduler / sweeper / action tests persist + query through this harness.
    public DbSet<NomNomzBot.Domain.Commands.Entities.ScheduledPipelineTask> ScheduledPipelineTasks =>
        Set<NomNomzBot.Domain.Commands.Entities.ScheduledPipelineTask>();
    public DbSet<NomNomzBot.Domain.Commands.Entities.PipelineStep> PipelineSteps =>
        Set<NomNomzBot.Domain.Commands.Entities.PipelineStep>();
    public DbSet<NomNomzBot.Domain.Commands.Entities.PipelineStepCondition> PipelineStepConditions =>
        Set<NomNomzBot.Domain.Commands.Entities.PipelineStepCondition>();
    public DbSet<NomNomzBot.Domain.Commands.Entities.PipelineTrigger> PipelineTriggers =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.PipelineExecution> PipelineExecutions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.PipelineRunState> PipelineRunStates =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.ChannelBuiltinCommand> ChannelBuiltinCommands =>
        Set<NomNomzBot.Domain.Commands.Entities.ChannelBuiltinCommand>();
    public DbSet<NomNomzBot.Domain.Commands.Entities.CommandCooldownState> CommandCooldownStates =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.NamedCounter> NamedCounters =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.ViewerData.Entities.ViewerDatum> ViewerData =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Engagement.Entities.EngagementConfig> EngagementConfigs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Engagement.Entities.ViewerEngagementState> ViewerEngagementStates =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.MediaShare.Entities.MediaShareConfig> MediaShareConfigs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.MediaShare.Entities.MediaShareRequest> MediaShareRequests =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.CommandUsage> CommandUsages =>
        Set<NomNomzBot.Domain.Commands.Entities.CommandUsage>();
    public DbSet<NomNomzBot.Domain.EventStore.Entities.EventJournal> EventJournals =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.EventStore.Entities.TenantSequence> TenantSequences =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.EventStore.Entities.ProjectionCheckpoint> ProjectionCheckpoints =>
        throw new NotSupportedException();

    // Roles & permissions (Plane A/B) — mapped (simple scalar/enum entities materialize on InMemory) so the
    // role-resolver tests can seed/query them through this harness.
    public DbSet<ChannelMembership> ChannelMemberships => Set<ChannelMembership>();
    public DbSet<ChannelCommunityStanding> ChannelCommunityStandings =>
        Set<ChannelCommunityStanding>();
    public DbSet<ActionDefinition> ActionDefinitions => Set<ActionDefinition>();
    public DbSet<ChannelActionOverride> ChannelActionOverrides => Set<ChannelActionOverride>();
    public DbSet<PermitGrant> PermitGrants => Set<PermitGrant>();
    public DbSet<ChannelMissingScope> ChannelMissingScopes => Set<ChannelMissingScope>();

    // Platform IAM (Plane C) — mapped (simple scalar entities) so the IAM-service tests seed through this harness.
    public DbSet<IamPermission> IamPermissions => Set<IamPermission>();
    public DbSet<IamRole> IamRoles => Set<IamRole>();
    public DbSet<IamRolePermission> IamRolePermissions => Set<IamRolePermission>();
    public DbSet<IamPrincipal> IamPrincipals => Set<IamPrincipal>();
    public DbSet<IamRoleAssignment> IamRoleAssignments => Set<IamRoleAssignment>();
    public DbSet<SecurityNotice> SecurityNotices => throw new NotSupportedException();
    public DbSet<IamAuditLog> IamAuditLogs => Set<IamAuditLog>();

    // Economy — currency core (mapped so the economy-service tests can seed/query through this harness).
    public DbSet<NomNomzBot.Domain.Economy.Entities.CurrencyConfig> CurrencyConfigs =>
        Set<NomNomzBot.Domain.Economy.Entities.CurrencyConfig>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.EarningRule> EarningRules =>
        Set<NomNomzBot.Domain.Economy.Entities.EarningRule>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.CurrencyAccount> CurrencyAccounts =>
        Set<NomNomzBot.Domain.Economy.Entities.CurrencyAccount>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.CurrencyLedgerEntry> CurrencyLedgerEntries =>
        Set<NomNomzBot.Domain.Economy.Entities.CurrencyLedgerEntry>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.CatalogItem> CatalogItems =>
        Set<NomNomzBot.Domain.Economy.Entities.CatalogItem>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.CatalogPurchase> CatalogPurchases =>
        Set<NomNomzBot.Domain.Economy.Entities.CatalogPurchase>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.GameConfig> GameConfigs =>
        Set<NomNomzBot.Domain.Economy.Entities.GameConfig>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.GamePlay> GamePlays =>
        Set<NomNomzBot.Domain.Economy.Entities.GamePlay>();
    public DbSet<NomNomzBot.Domain.Marketplace.Entities.InstalledBundle> InstalledBundles =>
        Set<NomNomzBot.Domain.Marketplace.Entities.InstalledBundle>();
    public DbSet<NomNomzBot.Domain.PlatformContent.Entities.PlatformContentDefinition> PlatformContentDefinitions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.PlatformContent.Entities.PlatformContentVersion> PlatformContentVersions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.PlatformContent.Entities.PlatformContentPublishJob> PlatformContentPublishJobs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.GameSession> GameSessions =>
        Set<NomNomzBot.Domain.Economy.Entities.GameSession>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.ViewerAgeConsent> ViewerAgeConsents =>
        Set<NomNomzBot.Domain.Economy.Entities.ViewerAgeConsent>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.SavingsJar> SavingsJars =>
        Set<NomNomzBot.Domain.Economy.Entities.SavingsJar>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.SavingsJarMembership> SavingsJarMemberships =>
        Set<NomNomzBot.Domain.Economy.Entities.SavingsJarMembership>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.JarContribution> JarContributions =>
        Set<NomNomzBot.Domain.Economy.Entities.JarContribution>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.LeaderboardConfig> LeaderboardConfigs =>
        Set<NomNomzBot.Domain.Economy.Entities.LeaderboardConfig>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.LeaderboardOptOut> LeaderboardOptOuts =>
        Set<NomNomzBot.Domain.Economy.Entities.LeaderboardOptOut>();
    public DbSet<NomNomzBot.Domain.Economy.Entities.LeaderboardSnapshot> LeaderboardSnapshots =>
        Set<NomNomzBot.Domain.Economy.Entities.LeaderboardSnapshot>();
    public DbSet<NomNomzBot.Domain.Billing.Entities.BillingTier> BillingTiers =>
        Set<NomNomzBot.Domain.Billing.Entities.BillingTier>();
    public DbSet<NomNomzBot.Domain.Billing.Entities.TierLimit> TierLimits =>
        Set<NomNomzBot.Domain.Billing.Entities.TierLimit>();
    public DbSet<NomNomzBot.Domain.Billing.Entities.Subscription> Subscriptions =>
        Set<NomNomzBot.Domain.Billing.Entities.Subscription>();
    public DbSet<NomNomzBot.Domain.Billing.Entities.Invoice> Invoices =>
        Set<NomNomzBot.Domain.Billing.Entities.Invoice>();
    public DbSet<NomNomzBot.Domain.Billing.Entities.UsageRecord> UsageRecords =>
        Set<NomNomzBot.Domain.Billing.Entities.UsageRecord>();
    public DbSet<NomNomzBot.Domain.Billing.Entities.FoundersBadge> FoundersBadges =>
        Set<NomNomzBot.Domain.Billing.Entities.FoundersBadge>();
    public DbSet<NomNomzBot.Domain.Billing.Entities.InviteCode> InviteCodes =>
        Set<NomNomzBot.Domain.Billing.Entities.InviteCode>();
    public DbSet<NomNomzBot.Domain.Federation.Entities.FederationPeer> FederationPeers =>
        Set<NomNomzBot.Domain.Federation.Entities.FederationPeer>();
    public DbSet<NomNomzBot.Domain.Federation.Entities.FederationPeerKey> FederationPeerKeys =>
        Set<NomNomzBot.Domain.Federation.Entities.FederationPeerKey>();
    public DbSet<NomNomzBot.Domain.Federation.Entities.ChannelFederationOptIn> ChannelFederationOptIns =>
        Set<NomNomzBot.Domain.Federation.Entities.ChannelFederationOptIn>();
    public DbSet<NomNomzBot.Domain.Webhooks.Entities.OutboundWebhookEndpoint> OutboundWebhookEndpoints =>
        Set<NomNomzBot.Domain.Webhooks.Entities.OutboundWebhookEndpoint>();
    public DbSet<NomNomzBot.Domain.Webhooks.Entities.OutboundWebhookDelivery> OutboundWebhookDeliveries =>
        Set<NomNomzBot.Domain.Webhooks.Entities.OutboundWebhookDelivery>();
    public DbSet<NomNomzBot.Domain.Webhooks.Entities.InboundWebhookEndpoint> InboundWebhookEndpoints =>
        Set<NomNomzBot.Domain.Webhooks.Entities.InboundWebhookEndpoint>();
    public DbSet<NomNomzBot.Domain.Platform.Entities.HttpEgressAllowlist> HttpEgressAllowlists =>
        Set<NomNomzBot.Domain.Platform.Entities.HttpEgressAllowlist>();
    public DbSet<NomNomzBot.Domain.Analytics.Entities.ViewerProfile> ViewerProfiles =>
        Set<NomNomzBot.Domain.Analytics.Entities.ViewerProfile>();
    public DbSet<NomNomzBot.Domain.Analytics.Entities.WatchSession> WatchSessions =>
        Set<NomNomzBot.Domain.Analytics.Entities.WatchSession>();
    public DbSet<NomNomzBot.Domain.Analytics.Entities.MessageActivityDaily> MessageActivityDailies =>
        Set<NomNomzBot.Domain.Analytics.Entities.MessageActivityDaily>();
    public DbSet<NomNomzBot.Domain.Analytics.Entities.ViewerEngagementDaily> ViewerEngagementDailies =>
        Set<NomNomzBot.Domain.Analytics.Entities.ViewerEngagementDaily>();
    public DbSet<NomNomzBot.Domain.Analytics.Entities.ChannelAnalyticsDaily> ChannelAnalyticsDailies =>
        Set<NomNomzBot.Domain.Analytics.Entities.ChannelAnalyticsDaily>();
    public DbSet<NomNomzBot.Domain.Analytics.Entities.ChannelChatterDay> ChannelChatterDays =>
        Set<NomNomzBot.Domain.Analytics.Entities.ChannelChatterDay>();

    // NOTE: the (BroadcasterId, ActivityDate[, ChatterHash]) unique indexes these two entities carry in
    // production (ChannelAnalyticsDailyConfiguration / ChannelChatterDayConfiguration) are declared in
    // OnModelCreating below — the InMemory provider never enforced them, so the projection's insert-race
    // handling was silently unverifiable until this harness moved to a real relational engine.
    public DbSet<NomNomzBot.Domain.Platform.Entities.FeatureFlag> FeatureFlags =>
        Set<NomNomzBot.Domain.Platform.Entities.FeatureFlag>();
    public DbSet<NomNomzBot.Domain.Platform.Entities.FeatureFlagOverride> FeatureFlagOverrides =>
        Set<NomNomzBot.Domain.Platform.Entities.FeatureFlagOverride>();
    public DbSet<NomNomzBot.Domain.CustomCode.Entities.CodeScript> CodeScripts =>
        Set<NomNomzBot.Domain.CustomCode.Entities.CodeScript>();
    public DbSet<NomNomzBot.Domain.CustomCode.Entities.CodeScriptVersion> CodeScriptVersions =>
        Set<NomNomzBot.Domain.CustomCode.Entities.CodeScriptVersion>();
    public DbSet<NomNomzBot.Domain.Sound.Entities.SoundClip> SoundClips =>
        Set<NomNomzBot.Domain.Sound.Entities.SoundClip>();
    public DbSet<NomNomzBot.Domain.Assets.Entities.ChannelAsset> ChannelAssets =>
        Set<NomNomzBot.Domain.Assets.Entities.ChannelAsset>();
    public DbSet<NomNomzBot.Domain.CustomEvents.Entities.CustomDataSource> CustomDataSources =>
        Set<NomNomzBot.Domain.CustomEvents.Entities.CustomDataSource>();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.ViewerReport> ViewerReports =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Supporters.Entities.SupporterConnection> SupporterConnections =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Supporters.Entities.SupporterEvent> SupporterEvents =>
        throw new NotSupportedException();
}
