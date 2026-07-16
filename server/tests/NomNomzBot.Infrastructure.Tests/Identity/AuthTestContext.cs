// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

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
using NomNomzBot.Infrastructure.Platform.Security;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Shared scaffolding for the auth behavior tests: a focused EF context mapping only the auth/integration
/// entities (so it runs on the InMemory provider, where the production <c>AppDbContext</c>'s
/// jsonb-of-complex-type columns cannot materialize), the REAL envelope-encryption crypto stack
/// (so the vault round-trip proves ciphertext-at-rest, not a stub), and a recording event bus.
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

    public static AuthDbContext NewContext() => NewContext(Guid.NewGuid().ToString());

    /// <summary>
    /// A context over a named in-memory store. Two contexts built with the SAME name share one backing store —
    /// the test analogue of a process restart against the same persisted database.
    /// </summary>
    public static AuthDbContext NewContext(string databaseName) =>
        new(new DbContextOptionsBuilder<AuthDbContext>().UseInMemoryDatabase(databaseName).Options);

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
        services.AddSingleton<ITokenProtector>(protector);
        ServiceProvider sp = services.BuildServiceProvider();
        return new SystemCredentialsProvider(
            sp.GetRequiredService<IServiceScopeFactory>(),
            configuration
        );
    }
}

/// <summary>Records every published domain event so a test can assert the side effect actually fired.</summary>
internal sealed class RecordingEventBus : IEventBus
{
    public List<IDomainEvent> Published { get; } = [];

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IDomainEvent
    {
        Published.Add(@event);
        return Task.CompletedTask;
    }

    public void PublishFireAndForget<TEvent>(TEvent @event)
        where TEvent : class, IDomainEvent => Published.Add(@event);
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
    public DbSet<NomNomzBot.Domain.Community.Entities.ChatPoll> ChatPolls =>
        Set<NomNomzBot.Domain.Community.Entities.ChatPoll>();
    public DbSet<NomNomzBot.Domain.Community.Entities.ChatPollVote> ChatPollVotes =>
        Set<NomNomzBot.Domain.Community.Entities.ChatPollVote>();
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<IntegrationConnection> IntegrationConnections => Set<IntegrationConnection>();
    public DbSet<IntegrationToken> IntegrationTokens => Set<IntegrationToken>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.CryptoKey> CryptoKeys =>
        Set<NomNomzBot.Domain.Identity.Entities.CryptoKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasKey(e => e.Id);
        b.Entity<User>().Ignore(e => e.Channel).Ignore(e => e.Pronoun);

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

        b.Entity<IntegrationToken>().HasKey(e => e.Id);
        b.Entity<IntegrationToken>().Ignore(e => e.Connection).Ignore(e => e.Channel);

        // The persisted DEK registry (scalar-only) — mapped so the vault/protector tests seal and re-open tokens
        // through the same store the production wiring uses, and so the restart-survival test can prove it.
        b.Entity<NomNomzBot.Domain.Identity.Entities.CryptoKey>().HasKey(e => e.Id);

        // Reactive missing-scope rows (scalar; nav ignored) — mapped so the ScopeNotificationService tests seed
        // + query gaps through this harness.
        b.Entity<NomNomzBot.Domain.Identity.Entities.ChannelMissingScope>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Identity.Entities.ChannelMissingScope>().Ignore(e => e.Channel);

        // System-config table (scalar Key/Value/SecureValue) — mapped so the system-credentials provider
        // tests can seed wizard-vaulted rows and prove the DB-first resolution + AAD binding.
        b.Entity<NomNomzBot.Domain.Platform.Entities.Configuration>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Platform.Entities.Configuration>().Ignore(e => e.Channel);

        // Mapped standalone (navs ignored, Channel.Moderators already ignored above) so the
        // ChannelAccessService tests can exercise the moderator-grant branch of tenant resolution.
        b.Entity<NomNomzBot.Domain.Identity.Entities.ChannelModerator>()
            .HasKey(e => new { e.ChannelId, e.UserId });
        b.Entity<NomNomzBot.Domain.Identity.Entities.ChannelModerator>()
            .Ignore(e => e.Channel)
            .Ignore(e => e.User);

        // Service is scalar-only (the Scopes string[] materializes on InMemory), so it maps cleanly (nav ignored)
        // — mapped so the IntegrationOAuthService mirror test can prove the connect flow writes the legacy
        // `Service` token row the music providers read.
        b.Entity<NomNomzBot.Domain.Platform.Entities.Service>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Platform.Entities.Service>().Ignore(e => e.Channel);

        // EF discovers entity types from the DbSet<T> property declarations regardless of the throwing
        // getter bodies, then tries to map their jsonb-of-complex-type columns (unsupported on InMemory).
        // Ignore every entity these tests do not exercise so the model stays minimal and provider-agnostic.
        b.Ignore<NomNomzBot.Domain.Widgets.Entities.Widget>();
        b.Ignore<NomNomzBot.Domain.Widgets.Entities.WidgetVersion>();
        b.Ignore<NomNomzBot.Domain.Widgets.Entities.WidgetGalleryItem>();
        b.Ignore<NomNomzBot.Domain.Widgets.Entities.WidgetGallerySubmissionEvent>();

        // Command: mapped scalar-only (both navs ignored; Aliases/TemplateResponses are primitive
        // collections that materialize on InMemory) so CommandUseCountHandler tests can prove the
        // UseCount/LastUsedAt fold through this harness.
        b.Entity<NomNomzBot.Domain.Commands.Entities.Command>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Commands.Entities.Command>()
            .Ignore(e => e.Channel)
            .Ignore(e => e.Pipeline);

        // Reward is scalar-only (no jsonb-of-complex-type column), so it materializes on InMemory. Mapped
        // (navs ignored) so the reward-sync tests can prove the Twitch read path through this harness.
        b.Entity<NomNomzBot.Domain.Rewards.Entities.Reward>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Rewards.Entities.Reward>().Ignore(e => e.Channel);
        b.Ignore<NomNomzBot.Domain.Platform.Entities.EventSubSubscription>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.EventSubConduit>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.EventSubConduitShard>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.IdempotencyKey>();

        // Stream / ChannelEvent / CommandUsage: mapped scalar-only (navs + primitive collections
        // ignored) so the per-stream analytics tests can seed a stream window and prove the folds.
        b.Entity<NomNomzBot.Domain.Stream.Entities.Stream>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Stream.Entities.Stream>()
            .Ignore(e => e.Channel)
            .Ignore(e => e.Tags)
            .Ignore(e => e.ContentLabels);
        b.Entity<NomNomzBot.Domain.Identity.Entities.ChannelEvent>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Identity.Entities.ChannelEvent>()
            .Ignore(e => e.Channel)
            .Ignore(e => e.User);
        b.Entity<NomNomzBot.Domain.Commands.Entities.CommandUsage>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Commands.Entities.CommandUsage>().Ignore(e => e.Command);

        // ChatMessage: mapped scalar-only (navs + jsonb fragment/badge collections ignored) so the
        // YouTube live-chat poll worker tests can prove the persisted-message dedupe through this harness.
        b.Entity<NomNomzBot.Domain.Chat.Entities.ChatMessage>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Chat.Entities.ChatMessage>()
            .Ignore(e => e.Channel)
            .Ignore(e => e.Stream)
            .Ignore(e => e.Fragments)
            .Ignore(e => e.Badges);
        b.Ignore<NomNomzBot.Domain.Platform.Entities.Storage>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.Record>();
        b.Ignore<NomNomzBot.Domain.Identity.Entities.Permission>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.ChannelFeature>();
        b.Ignore<NomNomzBot.Domain.Identity.Entities.ChannelBotAuthorization>();
        b.Ignore<NomNomzBot.Domain.Identity.Entities.IpcDevModeKey>();

        // BotAccount is scalar-only (no navigation properties at all), so it materializes on InMemory as-is.
        // Mapped so BotJoinOnOnboardingHandler tests can seed/query the shared platform bot through this harness.
        b.Entity<NomNomzBot.Domain.Identity.Entities.BotAccount>().HasKey(e => e.Id);
        b.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordGuildConnection>();
        b.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordNotificationConfig>();
        b.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordNotificationRole>();
        b.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordMemberOptIn>();
        b.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordNotificationDispatch>();
        b.Ignore<NomNomzBot.Domain.Identity.Entities.ChannelSubscription>();
        b.Ignore<NomNomzBot.Domain.Tts.Entities.TtsVoice>();
        b.Ignore<NomNomzBot.Domain.Tts.Entities.UserTtsVoice>();
        b.Ignore<NomNomzBot.Domain.Tts.Entities.TtsUsageRecord>();
        b.Ignore<NomNomzBot.Domain.Tts.Entities.TtsCacheEntry>();
        b.Ignore<NomNomzBot.Domain.Identity.Entities.Pronoun>();
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
        b.Ignore<NomNomzBot.Domain.Commands.Entities.PipelineStep>();
        b.Ignore<NomNomzBot.Domain.EventStore.Entities.EventJournal>();
        b.Ignore<NomNomzBot.Domain.EventStore.Entities.TenantSequence>();
        b.Ignore<NomNomzBot.Domain.EventStore.Entities.ProjectionCheckpoint>();
    }

    // ── Unused IApplicationDbContext surface — never reached by these tests ──
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelModerator> ChannelModerators =>
        Set<NomNomzBot.Domain.Identity.Entities.ChannelModerator>();
    public DbSet<NomNomzBot.Domain.Platform.Entities.Service> Services =>
        Set<NomNomzBot.Domain.Platform.Entities.Service>();
    public DbSet<NomNomzBot.Domain.Commands.Entities.Command> Commands =>
        Set<NomNomzBot.Domain.Commands.Entities.Command>();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.Reward> Rewards =>
        Set<NomNomzBot.Domain.Rewards.Entities.Reward>();
    public DbSet<NomNomzBot.Domain.Quotes.Entities.Quote> Quotes =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.PickLists.Entities.PickList> PickLists =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Widgets.Entities.Widget> Widgets =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Widgets.Entities.WidgetVersion> WidgetVersions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Widgets.Entities.WidgetGalleryItem> WidgetGalleryItems =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Widgets.Entities.WidgetGallerySubmissionEvent> WidgetGallerySubmissionEvents =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.EventSubSubscription> EventSubSubscriptions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.EventSubConduit> EventSubConduits =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.EventSubConduitShard> EventSubConduitShards =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.IdempotencyKey> IdempotencyKeys =>
        throw new NotSupportedException();
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
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelEvent> ChannelEvents =>
        Set<NomNomzBot.Domain.Identity.Entities.ChannelEvent>();
    public DbSet<NomNomzBot.Domain.Stream.Entities.Stream> Streams =>
        Set<NomNomzBot.Domain.Stream.Entities.Stream>();
    public DbSet<NomNomzBot.Domain.Platform.Entities.Configuration> Configurations =>
        Set<NomNomzBot.Domain.Platform.Entities.Configuration>();
    public DbSet<NomNomzBot.Domain.Platform.Entities.Storage> Storages =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.Record> Records =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.Permission> Permissions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.ChannelFeature> ChannelFeatures =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelBotAuthorization> ChannelBotAuthorizations =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.BotAccount> BotAccounts =>
        Set<NomNomzBot.Domain.Identity.Entities.BotAccount>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IpcDevModeKey> IpcDevModeKeys =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordGuildConnection> DiscordGuildConnections =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordNotificationConfig> DiscordNotificationConfigs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordNotificationRole> DiscordNotificationRoles =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordMemberOptIn> DiscordMemberOptIns =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordNotificationDispatch> DiscordNotificationDispatches =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelSubscription> ChannelSubscriptions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsVoice> TtsVoices =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.UserTtsVoice> UserTtsVoices =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsUsageRecord> TtsUsageRecords =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsCacheEntry> TtsCacheEntries =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsApprovalQueueEntry> TtsApprovalQueueEntries =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.Pronoun> Pronouns =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.DeletionAuditLog> DeletionAuditLogs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.Timer> Timers =>
        Set<NomNomzBot.Domain.Commands.Entities.Timer>();
    public DbSet<NomNomzBot.Domain.Commands.Entities.EventResponse> EventResponses =>
        Set<NomNomzBot.Domain.Commands.Entities.EventResponse>();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.WatchStreak> WatchStreaks =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.Pipeline> Pipelines =>
        Set<NomNomzBot.Domain.Commands.Entities.Pipeline>();
    public DbSet<NomNomzBot.Domain.Commands.Entities.PipelineStep> PipelineSteps =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.PipelineStepCondition> PipelineStepConditions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.PipelineExecution> PipelineExecutions =>
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
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelMembership> ChannelMemberships =>
        Set<NomNomzBot.Domain.Identity.Entities.ChannelMembership>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelCommunityStanding> ChannelCommunityStandings =>
        Set<NomNomzBot.Domain.Identity.Entities.ChannelCommunityStanding>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ActionDefinition> ActionDefinitions =>
        Set<NomNomzBot.Domain.Identity.Entities.ActionDefinition>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelActionOverride> ChannelActionOverrides =>
        Set<NomNomzBot.Domain.Identity.Entities.ChannelActionOverride>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.PermitGrant> PermitGrants =>
        Set<NomNomzBot.Domain.Identity.Entities.PermitGrant>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelMissingScope> ChannelMissingScopes =>
        Set<NomNomzBot.Domain.Identity.Entities.ChannelMissingScope>();

    // Platform IAM (Plane C) — mapped (simple scalar entities) so the IAM-service tests seed through this harness.
    public DbSet<NomNomzBot.Domain.Identity.Entities.IamPermission> IamPermissions =>
        Set<NomNomzBot.Domain.Identity.Entities.IamPermission>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IamRole> IamRoles =>
        Set<NomNomzBot.Domain.Identity.Entities.IamRole>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IamRolePermission> IamRolePermissions =>
        Set<NomNomzBot.Domain.Identity.Entities.IamRolePermission>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IamPrincipal> IamPrincipals =>
        Set<NomNomzBot.Domain.Identity.Entities.IamPrincipal>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IamRoleAssignment> IamRoleAssignments =>
        Set<NomNomzBot.Domain.Identity.Entities.IamRoleAssignment>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IamAuditLog> IamAuditLogs =>
        Set<NomNomzBot.Domain.Identity.Entities.IamAuditLog>();

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
    public DbSet<NomNomzBot.Domain.Platform.Entities.FeatureFlag> FeatureFlags =>
        Set<NomNomzBot.Domain.Platform.Entities.FeatureFlag>();
    public DbSet<NomNomzBot.Domain.Platform.Entities.FeatureFlagOverride> FeatureFlagOverrides =>
        Set<NomNomzBot.Domain.Platform.Entities.FeatureFlagOverride>();
    public DbSet<NomNomzBot.Domain.CustomCode.Entities.CodeScript> CodeScripts =>
        Set<NomNomzBot.Domain.CustomCode.Entities.CodeScript>();
    public DbSet<NomNomzBot.Domain.CustomCode.Entities.CodeScriptVersion> CodeScriptVersions =>
        Set<NomNomzBot.Domain.CustomCode.Entities.CodeScriptVersion>();
    public DbSet<NomNomzBot.Domain.Sound.Entities.SoundClip> SoundClips =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.CustomEvents.Entities.CustomDataSource> CustomDataSources =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.ViewerReport> ViewerReports =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Supporters.Entities.SupporterConnection> SupporterConnections =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Supporters.Entities.SupporterEvent> SupporterEvents =>
        throw new NotSupportedException();
}
