// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Domain.Assets.Entities;
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.CustomCode.Entities;
using NomNomzBot.Domain.CustomEvents.Entities;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Federation.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.PickLists.Entities;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Quotes.Entities;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Sound.Entities;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Platform.Persistence.Extensions;

namespace NomNomzBot.Infrastructure.Tests.Platform.Pipeline;

/// <summary>
/// A focused <see cref="IApplicationDbContext"/> over the entities the S-RICH-PICKERS option providers read
/// (Reward, TtsVoice, SoundClip, Widget, ChannelAsset, DiscordGuildConnection, ViewerProfile, User, Channel) —
/// on a real relational SQLite database. Mirrors the "declare every DbSet, auto-ignore the unmapped ones" shape as
/// <c>Commands/CommandsTestDbContext.cs</c>.
/// </summary>
internal sealed class PipelineOptionsTestDbContext : DbContext, IApplicationDbContext
{
    // One private, non-shared in-memory SQLite connection per context instance - a REAL relational
    // database (S-API-TESTS-INMEMORY; the EF InMemory provider is retired here because it ignores unique
    // indexes, FK constraints and query translation, so it green-lights writes the real database rejects).
    // Opened by New(), closed by this context's own Dispose/DisposeAsync overrides.
    private readonly SqliteConnection _connection;

    private PipelineOptionsTestDbContext(
        DbContextOptions<PipelineOptionsTestDbContext> options,
        SqliteConnection connection
    )
        : base(options) => _connection = connection;

    public static PipelineOptionsTestDbContext New()
    {
        SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();
        PipelineOptionsTestDbContext db = new(
            new DbContextOptionsBuilder<PipelineOptionsTestDbContext>()
                .UseSqlite(connection)
                .Options,
            connection
        );
        db.Database.EnsureCreated();
        return db;
    }

    public override void Dispose()
    {
        base.Dispose();
        _connection.Dispose();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _connection.DisposeAsync();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Reward>(e =>
        {
            e.HasKey(r => r.Id);
            e.Ignore(r => r.Channel);
        });

        b.Entity<TtsVoice>(e => e.HasKey(v => v.Id));

        b.Entity<SoundClip>(e =>
        {
            e.HasKey(c => c.Id);
            e.Ignore(c => c.Channel);
            e.Ignore(c => c.CreatedByUser);
        });

        b.Entity<Widget>(e =>
        {
            e.HasKey(w => w.Id);
            e.Ignore(w => w.Channel);
            e.Ignore(w => w.Settings);
        });

        b.Entity<ChannelAsset>(e =>
        {
            e.HasKey(a => a.Id);
            e.Ignore(a => a.Channel);
            e.Ignore(a => a.CreatedByUser);
        });

        b.Entity<DiscordGuildConnection>(e =>
        {
            e.HasKey(c => c.Id);
            e.Ignore(c => c.Channel);
        });

        b.Entity<ViewerProfile>(e => e.HasKey(p => p.Id));

        b.Entity<User>(e => e.HasKey(u => u.Id));

        b.Entity<Channel>(e =>
        {
            e.HasKey(c => c.Id);
            e.Ignore(c => c.User);
            e.Ignore(c => c.Moderators);
            e.Ignore(c => c.Streams);
            e.Ignore(c => c.Events);
        });

        // EF discovers entity types from the DbSet<T> property declarations regardless of the throwing getter
        // bodies; ignore every entity these tests do not exercise so the model stays minimal + provider-agnostic.
        foreach (Type entity in UnmappedEntities)
            b.Ignore(entity);

        b.ApplySqliteCompatibility();
    }

    private static readonly HashSet<Type> Mapped =
    [
        typeof(Reward),
        typeof(TtsVoice),
        typeof(SoundClip),
        typeof(Widget),
        typeof(ChannelAsset),
        typeof(DiscordGuildConnection),
        typeof(ViewerProfile),
        typeof(User),
        typeof(Channel),
    ];

    private static readonly IReadOnlyList<Type> UnmappedEntities =
    [
        .. typeof(IApplicationDbContext)
            .GetProperties()
            .Where(p =>
                p.PropertyType.IsGenericType
                && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
            )
            .Select(p => p.PropertyType.GetGenericArguments()[0])
            .Where(t => !Mapped.Contains(t)),
    ];

    // ── Full IApplicationDbContext surface — unused DbSets resolve fine but are never seeded/queried ─────────
    public DbSet<User> Users => Set<User>();
    public DbSet<UserIdentity> UserIdentities => Set<UserIdentity>();
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<PlatformConnection> PlatformConnections => Set<PlatformConnection>();
    public DbSet<ChannelModerator> ChannelModerators => Set<ChannelModerator>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Command> Commands => Set<Command>();
    public DbSet<Reward> Rewards => Set<Reward>();
    public DbSet<Redemption> Redemptions => Set<Redemption>();
    public DbSet<RedemptionTimer> RedemptionTimers => Set<RedemptionTimer>();
    public DbSet<ChatTrigger> ChatTriggers => Set<ChatTrigger>();
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
        Set<NomNomzBot.Domain.Moderation.Entities.ChatFilter>();
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
    public DbSet<NomNomzBot.Domain.Community.Entities.ChatPoll> ChatPolls =>
        Set<NomNomzBot.Domain.Community.Entities.ChatPoll>();
    public DbSet<NomNomzBot.Domain.Community.Entities.ChatPollVote> ChatPollVotes =>
        Set<NomNomzBot.Domain.Community.Entities.ChatPollVote>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<NomNomzBot.Domain.Music.Entities.BlockedTrack> BlockedTracks =>
        Set<NomNomzBot.Domain.Music.Entities.BlockedTrack>();
    public DbSet<PickList> PickLists => Set<PickList>();
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
    public DbSet<Widget> Widgets => Set<Widget>();
    public DbSet<WidgetVersion> WidgetVersions => Set<WidgetVersion>();
    public DbSet<WidgetGalleryItem> WidgetGalleryItems => Set<WidgetGalleryItem>();
    public DbSet<WidgetGallerySubmissionEvent> WidgetGallerySubmissionEvents =>
        Set<WidgetGallerySubmissionEvent>();
    public DbSet<RenderedAlertCapture> RenderedAlertCaptures => throw new NotSupportedException();
    public DbSet<EventSubSubscription> EventSubSubscriptions => Set<EventSubSubscription>();
    public DbSet<EventSubConduit> EventSubConduits => Set<EventSubConduit>();
    public DbSet<EventSubConduitShard> EventSubConduitShards => Set<EventSubConduitShard>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<YouTubeLiveChatBan> YouTubeLiveChatBans => Set<YouTubeLiveChatBan>();
    public DbSet<ChannelEvent> ChannelEvents => Set<ChannelEvent>();
    public DbSet<NomNomzBot.Domain.Stream.Entities.Stream> Streams =>
        Set<NomNomzBot.Domain.Stream.Entities.Stream>();
    public DbSet<NomNomzBot.Domain.Platform.Entities.Configuration> Configurations =>
        Set<NomNomzBot.Domain.Platform.Entities.Configuration>();
    public DbSet<Storage> Storages => Set<Storage>();
    public DbSet<NomNomzBot.Domain.Platform.Entities.Record> Records =>
        Set<NomNomzBot.Domain.Platform.Entities.Record>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<ChannelFeature> ChannelFeatures => Set<ChannelFeature>();
    public DbSet<ChannelBotAuthorization> ChannelBotAuthorizations =>
        Set<ChannelBotAuthorization>();
    public DbSet<BotAccount> BotAccounts => Set<BotAccount>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<IpcDevModeKey> IpcDevModeKeys => Set<IpcDevModeKey>();
    public DbSet<IntegrationConnection> IntegrationConnections => Set<IntegrationConnection>();
    public DbSet<IntegrationToken> IntegrationTokens => Set<IntegrationToken>();
    public DbSet<CryptoKey> CryptoKeys => Set<CryptoKey>();
    public DbSet<KeyUsageBinding> KeyUsageBindings => Set<KeyUsageBinding>();
    public DbSet<DiscordGuildConnection> DiscordGuildConnections => Set<DiscordGuildConnection>();
    public DbSet<DiscordNotificationConfig> DiscordNotificationConfigs =>
        Set<DiscordNotificationConfig>();
    public DbSet<DiscordNotificationRole> DiscordNotificationRoles =>
        Set<DiscordNotificationRole>();
    public DbSet<DiscordMemberOptIn> DiscordMemberOptIns => Set<DiscordMemberOptIn>();
    public DbSet<DiscordLiveRoleConfig> DiscordLiveRoleConfigs => throw new NotSupportedException();
    public DbSet<DiscordNotificationDispatch> DiscordNotificationDispatches =>
        Set<DiscordNotificationDispatch>();
    public DbSet<ChannelSubscription> ChannelSubscriptions => Set<ChannelSubscription>();
    public DbSet<Domain.Automation.Entities.AutomationApiToken> AutomationApiTokens =>
        Set<Domain.Automation.Entities.AutomationApiToken>();
    public DbSet<NomNomzBot.Domain.Obs.Entities.ObsConnection> ObsConnections =>
        Set<NomNomzBot.Domain.Obs.Entities.ObsConnection>();
    public DbSet<NomNomzBot.Domain.Vts.Entities.VtsConnection> VtsConnections =>
        Set<NomNomzBot.Domain.Vts.Entities.VtsConnection>();
    public DbSet<TtsConfig> TtsConfigs => Set<TtsConfig>();
    public DbSet<TtsVoice> TtsVoices => Set<TtsVoice>();
    public DbSet<UserTtsVoice> UserTtsVoices => Set<UserTtsVoice>();
    public DbSet<TtsUsageRecord> TtsUsageRecords => Set<TtsUsageRecord>();
    public DbSet<TtsCacheEntry> TtsCacheEntries => Set<TtsCacheEntry>();
    public DbSet<TtsLexiconEntry> TtsLexiconEntries => Set<TtsLexiconEntry>();
    public DbSet<TtsApprovalQueueEntry> TtsApprovalQueueEntries => Set<TtsApprovalQueueEntry>();
    public DbSet<Pronoun> Pronouns => Set<Pronoun>();
    public DbSet<DeletionAuditLog> DeletionAuditLogs => Set<DeletionAuditLog>();
    public DbSet<NomNomzBot.Domain.Stream.Entities.ShoutoutOverride> ShoutoutOverrides =>
        throw new NotSupportedException();
    public DbSet<ErasureRequest> ErasureRequests => Set<ErasureRequest>();
    public DbSet<ComplianceAuditLog> ComplianceAuditLogs => Set<ComplianceAuditLog>();
    public DbSet<NomNomzBot.Domain.Commands.Entities.Timer> Timers =>
        Set<NomNomzBot.Domain.Commands.Entities.Timer>();
    public DbSet<EventResponse> EventResponses => Set<EventResponse>();
    public DbSet<WatchStreak> WatchStreaks => Set<WatchStreak>();
    public DbSet<NomNomzBot.Domain.Commands.Entities.Pipeline> Pipelines =>
        Set<NomNomzBot.Domain.Commands.Entities.Pipeline>();
    public DbSet<ScheduledPipelineTask> ScheduledPipelineTasks => Set<ScheduledPipelineTask>();
    public DbSet<PipelineStep> PipelineSteps => Set<PipelineStep>();
    public DbSet<PipelineStepCondition> PipelineStepConditions => Set<PipelineStepCondition>();
    public DbSet<PipelineTrigger> PipelineTriggers => Set<PipelineTrigger>();
    public DbSet<PipelineExecution> PipelineExecutions => Set<PipelineExecution>();
    public DbSet<PipelineRunState> PipelineRunStates => throw new NotSupportedException();
    public DbSet<ChannelBuiltinCommand> ChannelBuiltinCommands => Set<ChannelBuiltinCommand>();
    public DbSet<CommandCooldownState> CommandCooldownStates => Set<CommandCooldownState>();
    public DbSet<NamedCounter> NamedCounters => Set<NamedCounter>();
    public DbSet<NomNomzBot.Domain.ViewerData.Entities.ViewerDatum> ViewerData =>
        Set<NomNomzBot.Domain.ViewerData.Entities.ViewerDatum>();
    public DbSet<NomNomzBot.Domain.Engagement.Entities.EngagementConfig> EngagementConfigs =>
        Set<NomNomzBot.Domain.Engagement.Entities.EngagementConfig>();
    public DbSet<NomNomzBot.Domain.Engagement.Entities.ViewerEngagementState> ViewerEngagementStates =>
        Set<NomNomzBot.Domain.Engagement.Entities.ViewerEngagementState>();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.ViewerReport> ViewerReports =>
        Set<NomNomzBot.Domain.Moderation.Entities.ViewerReport>();
    public DbSet<NomNomzBot.Domain.MediaShare.Entities.MediaShareConfig> MediaShareConfigs =>
        Set<NomNomzBot.Domain.MediaShare.Entities.MediaShareConfig>();
    public DbSet<NomNomzBot.Domain.MediaShare.Entities.MediaShareRequest> MediaShareRequests =>
        Set<NomNomzBot.Domain.MediaShare.Entities.MediaShareRequest>();
    public DbSet<NomNomzBot.Domain.Supporters.Entities.SupporterConnection> SupporterConnections =>
        Set<NomNomzBot.Domain.Supporters.Entities.SupporterConnection>();
    public DbSet<NomNomzBot.Domain.Supporters.Entities.SupporterEvent> SupporterEvents =>
        Set<NomNomzBot.Domain.Supporters.Entities.SupporterEvent>();
    public DbSet<CommandUsage> CommandUsages => Set<CommandUsage>();
    public DbSet<EventJournal> EventJournals => Set<EventJournal>();
    public DbSet<TenantSequence> TenantSequences => Set<TenantSequence>();
    public DbSet<ProjectionCheckpoint> ProjectionCheckpoints => Set<ProjectionCheckpoint>();
    public DbSet<EventSubjectKey> EventSubjectKeys => Set<EventSubjectKey>();
    public DbSet<ChannelMembership> ChannelMemberships => Set<ChannelMembership>();
    public DbSet<ChannelCommunityStanding> ChannelCommunityStandings =>
        Set<ChannelCommunityStanding>();
    public DbSet<ActionDefinition> ActionDefinitions => Set<ActionDefinition>();
    public DbSet<ChannelActionOverride> ChannelActionOverrides => Set<ChannelActionOverride>();
    public DbSet<PermitGrant> PermitGrants => Set<PermitGrant>();
    public DbSet<ChannelMissingScope> ChannelMissingScopes => Set<ChannelMissingScope>();
    public DbSet<IamPermission> IamPermissions => Set<IamPermission>();
    public DbSet<IamRole> IamRoles => Set<IamRole>();
    public DbSet<IamRolePermission> IamRolePermissions => Set<IamRolePermission>();
    public DbSet<IamPrincipal> IamPrincipals => Set<IamPrincipal>();
    public DbSet<IamRoleAssignment> IamRoleAssignments => Set<IamRoleAssignment>();
    public DbSet<SecurityNotice> SecurityNotices => throw new NotSupportedException();
    public DbSet<IamAuditLog> IamAuditLogs => Set<IamAuditLog>();
    public DbSet<CurrencyConfig> CurrencyConfigs => Set<CurrencyConfig>();
    public DbSet<EarningRule> EarningRules => Set<EarningRule>();
    public DbSet<CurrencyAccount> CurrencyAccounts => Set<CurrencyAccount>();
    public DbSet<CurrencyLedgerEntry> CurrencyLedgerEntries => Set<CurrencyLedgerEntry>();
    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();
    public DbSet<CatalogPurchase> CatalogPurchases => Set<CatalogPurchase>();
    public DbSet<GameConfig> GameConfigs => Set<GameConfig>();
    public DbSet<GamePlay> GamePlays => Set<GamePlay>();
    public DbSet<GameSession> GameSessions => Set<GameSession>();
    public DbSet<ViewerAgeConsent> ViewerAgeConsents => Set<ViewerAgeConsent>();
    public DbSet<SavingsJar> SavingsJars => Set<SavingsJar>();
    public DbSet<SavingsJarMembership> SavingsJarMemberships => Set<SavingsJarMembership>();
    public DbSet<JarContribution> JarContributions => Set<JarContribution>();
    public DbSet<LeaderboardConfig> LeaderboardConfigs => Set<LeaderboardConfig>();
    public DbSet<LeaderboardOptOut> LeaderboardOptOuts => Set<LeaderboardOptOut>();
    public DbSet<LeaderboardSnapshot> LeaderboardSnapshots => Set<LeaderboardSnapshot>();
    public DbSet<BillingTier> BillingTiers => Set<BillingTier>();
    public DbSet<TierLimit> TierLimits => Set<TierLimit>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<UsageRecord> UsageRecords => Set<UsageRecord>();
    public DbSet<FoundersBadge> FoundersBadges => Set<FoundersBadge>();
    public DbSet<InviteCode> InviteCodes => Set<InviteCode>();
    public DbSet<FederationPeer> FederationPeers => Set<FederationPeer>();
    public DbSet<FederationPeerKey> FederationPeerKeys => Set<FederationPeerKey>();
    public DbSet<ChannelFederationOptIn> ChannelFederationOptIns => Set<ChannelFederationOptIn>();
    public DbSet<OutboundWebhookEndpoint> OutboundWebhookEndpoints =>
        Set<OutboundWebhookEndpoint>();
    public DbSet<OutboundWebhookDelivery> OutboundWebhookDeliveries =>
        Set<OutboundWebhookDelivery>();
    public DbSet<InboundWebhookEndpoint> InboundWebhookEndpoints => Set<InboundWebhookEndpoint>();
    public DbSet<HttpEgressAllowlist> HttpEgressAllowlists => Set<HttpEgressAllowlist>();
    public DbSet<ViewerProfile> ViewerProfiles => Set<ViewerProfile>();
    public DbSet<WatchSession> WatchSessions => Set<WatchSession>();
    public DbSet<MessageActivityDaily> MessageActivityDailies => Set<MessageActivityDaily>();
    public DbSet<ViewerEngagementDaily> ViewerEngagementDailies => Set<ViewerEngagementDaily>();
    public DbSet<ChannelAnalyticsDaily> ChannelAnalyticsDailies => Set<ChannelAnalyticsDaily>();
    public DbSet<ChannelChatterDay> ChannelChatterDays => Set<ChannelChatterDay>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<FeatureFlagOverride> FeatureFlagOverrides => Set<FeatureFlagOverride>();
    public DbSet<CodeScript> CodeScripts => Set<CodeScript>();
    public DbSet<CodeScriptVersion> CodeScriptVersions => Set<CodeScriptVersion>();
    public DbSet<SoundClip> SoundClips => Set<SoundClip>();
    public DbSet<ChannelAsset> ChannelAssets => Set<ChannelAsset>();
    public DbSet<CustomDataSource> CustomDataSources => Set<CustomDataSource>();
    public DbSet<NomNomzBot.Domain.Marketplace.Entities.InstalledBundle> InstalledBundles =>
        Set<NomNomzBot.Domain.Marketplace.Entities.InstalledBundle>();
}
