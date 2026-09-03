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
using NomNomzBot.Domain.Assets.Entities;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.CustomCode.Entities;
using NomNomzBot.Domain.CustomEvents.Entities;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Giveaways.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Marketplace.Entities;
using NomNomzBot.Domain.PickLists.Entities;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Sound.Entities;
using NomNomzBot.Domain.Supporters.Entities;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Platform.Persistence.Extensions;
using NomNomzBot.Infrastructure.Platform.Persistence.Interceptors;

namespace NomNomzBot.Infrastructure.Tests.Consequences;

/// <summary>
/// A focused <see cref="IApplicationDbContext"/> over exactly the tables the S-CONSEQ delete previews COUNT:
/// the resource being deleted (sound clip, widget, reward, giveaway code pool) and every row that references
/// it (widget versions, redemptions, redemption timers, giveaway codes, giveaways) plus the pipeline steps
/// whose <c>ConfigJson</c> names a keyless resource. Runs on a real relational SQLite connection so the counts
/// under test are produced by real SQL over real rows — an in-memory list would not prove the tenant filter.
/// The production <c>AppDbContext</c> is Npgsql-bound (jsonb complex types) and cannot host a test provider,
/// so only the counted slice is mapped; everything else throws.
/// </summary>
internal sealed class BlastRadiusTestDbContext : DbContext, IApplicationDbContext
{
    public BlastRadiusTestDbContext(DbContextOptions<BlastRadiusTestDbContext> options)
        : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Channel>(b =>
        {
            b.HasKey(c => c.Id);
            b.Ignore(c => c.Tags);
            b.Ignore(c => c.ContentLabels);
            b.Ignore(c => c.User);
            b.Ignore(c => c.Moderators);
            b.Ignore(c => c.Streams);
            b.Ignore(c => c.Events);
        });

        modelBuilder.Entity<Widget>(b =>
        {
            b.HasKey(w => w.Id);
            b.Ignore(w => w.Settings);
            b.Ignore(w => w.EventSubscriptions);
            b.Ignore(w => w.Channel);
        });
        modelBuilder.Entity<WidgetVersion>(b => b.HasKey(v => v.Id));

        modelBuilder.Entity<Pipeline>(b =>
        {
            b.HasKey(p => p.Id);
            b.Ignore(p => p.Steps);
        });
        modelBuilder.Entity<PipelineStep>(b =>
        {
            b.HasKey(s => s.Id);
            b.Ignore(s => s.Conditions);
            b.Ignore(s => s.Pipeline);
        });

        modelBuilder.Entity<SoundClip>(b =>
        {
            b.HasKey(c => c.Id);
            b.Ignore(c => c.Channel);
            b.Ignore(c => c.CreatedByUser);
        });

        modelBuilder.Entity<Reward>(b => b.HasKey(r => r.Id));
        modelBuilder.Entity<Redemption>(b => b.HasKey(r => r.Id));
        modelBuilder.Entity<RedemptionTimer>(b => b.HasKey(t => t.Id));

        modelBuilder.Entity<GiveawayEntry>(b => b.HasKey(e => e.Id));
        modelBuilder.Entity<GiveawayWinner>(b => b.HasKey(w => w.Id));

        modelBuilder.Entity<PickList>(b =>
        {
            b.HasKey(l => l.Id);
            b.Ignore(l => l.Items);
            b.Ignore(l => l.Channel);
        });

        modelBuilder.Entity<ChannelAsset>(b =>
        {
            b.HasKey(a => a.Id);
            b.Ignore(a => a.Channel);
            b.Ignore(a => a.CreatedByUser);
        });

        modelBuilder.Entity<CustomDataSource>(b =>
        {
            b.HasKey(d => d.Id);
            b.Ignore(d => d.Channel);
            b.Ignore(d => d.CreatedByUser);
            b.Ignore(d => d.InboundWebhookEndpoint);
        });

        modelBuilder.Entity<EventResponse>(b =>
        {
            b.HasKey(e => e.Id);
            b.Ignore(e => e.MetadataJson);
            b.Ignore(e => e.Pipeline);
            b.Ignore(e => e.Channel);
        });
        modelBuilder.Entity<Command>(b =>
        {
            b.HasKey(c => c.Id);
            b.Ignore(c => c.Aliases);
            b.Ignore(c => c.TemplateResponses);
            b.Ignore(c => c.Pipeline);
            b.Ignore(c => c.Channel);
        });
        modelBuilder.Entity<NomNomzBot.Domain.Commands.Entities.Timer>(b =>
        {
            b.HasKey(t => t.Id);
            b.Ignore(t => t.Messages);
            b.Ignore(t => t.Pipeline);
            b.Ignore(t => t.Channel);
        });
        modelBuilder.Entity<ChatTrigger>(b =>
        {
            b.HasKey(t => t.Id);
            b.Ignore(t => t.Pipeline);
            b.Ignore(t => t.Channel);
        });

        modelBuilder.Entity<CatalogItem>(b => b.HasKey(i => i.Id));
        modelBuilder.Entity<CatalogPurchase>(b => b.HasKey(i => i.Id));
        modelBuilder.Entity<LeaderboardConfig>(b => b.HasKey(c => c.Id));
        modelBuilder.Entity<LeaderboardSnapshot>(b => b.HasKey(c => c.Id));

        modelBuilder.Entity<CodeScript>(b => b.HasKey(c => c.Id));
        modelBuilder.Entity<CodeScriptVersion>(b => b.HasKey(v => v.Id));

        modelBuilder.Entity<InboundWebhookEndpoint>(b => b.HasKey(e => e.Id));
        modelBuilder.Entity<SupporterConnection>(b =>
        {
            b.HasKey(c => c.Id);
            b.Ignore(c => c.Channel);
        });

        modelBuilder.Entity<InstalledBundle>(b =>
        {
            b.HasKey(x => x.Id);
            b.Ignore(x => x.Channel);
            b.Ignore(x => x.InstalledByUser);
        });

        modelBuilder.Entity<DiscordGuildConnection>(b =>
        {
            b.HasKey(c => c.Id);
            b.Ignore(c => c.Channel);
        });
        modelBuilder.Entity<DiscordNotificationConfig>(b =>
        {
            b.HasKey(c => c.Id);
            b.Ignore(c => c.EmbedConfig);
            b.Ignore(c => c.Channel);
            b.Ignore(c => c.GuildConnection);
        });
        modelBuilder.Entity<DiscordNotificationRole>(b =>
        {
            b.HasKey(r => r.Id);
            b.Ignore(r => r.Channel);
            b.Ignore(r => r.GuildConnection);
        });

        modelBuilder.Entity<IntegrationConnection>(b =>
        {
            b.HasKey(c => c.Id);
            b.Ignore(c => c.Channel);
            b.Ignore(c => c.Tokens);
            b.Ignore(c => c.Scopes);
        });
        modelBuilder.Entity<Service>(b =>
        {
            b.HasKey(x => x.Id);
            b.Ignore(x => x.Channel);
        });

        modelBuilder.Entity<EventSubSubscription>(b =>
        {
            b.HasKey(s => s.Id);
            b.Ignore(s => s.Condition);
        });
        modelBuilder.Entity<OutboundWebhookEndpoint>(b => b.HasKey(e => e.Id));

        modelBuilder.Entity<Giveaway>(b => b.HasKey(g => g.Id));
        modelBuilder.Entity<GiveawayCodePool>(b => b.HasKey(p => p.Id));
        modelBuilder.Entity<GiveawayCode>(b => b.HasKey(c => c.Id));

        // EF discovers an entity type from EVERY DbSet<T> property on the context (an IApplicationDbContext
        // requirement) — even the throwing ones. Ignore every entity outside this slice so the model stays
        // minimal and provider-agnostic.
        foreach (Type entity in UnmappedEntities)
            modelBuilder.Ignore(entity);

        // The production soft-delete global filter (schema 1.2) — a deleted row must disappear from the
        // counts exactly the way it does in production.
        modelBuilder.ApplySoftDeleteFilter<Widget>();
        modelBuilder.ApplySoftDeleteFilter<SoundClip>();
        modelBuilder.ApplySoftDeleteFilter<Reward>();
        modelBuilder.ApplySoftDeleteFilter<Giveaway>();
        modelBuilder.ApplySoftDeleteFilter<GiveawayCodePool>();
        modelBuilder.ApplySoftDeleteFilter<GiveawayCode>();
        modelBuilder.ApplySoftDeleteFilter<GiveawayEntry>();
        modelBuilder.ApplySoftDeleteFilter<PickList>();
        modelBuilder.ApplySoftDeleteFilter<ChannelAsset>();
        modelBuilder.ApplySoftDeleteFilter<CustomDataSource>();
        modelBuilder.ApplySoftDeleteFilter<CodeScript>();
        modelBuilder.ApplySoftDeleteFilter<InstalledBundle>();
        modelBuilder.ApplySoftDeleteFilter<DiscordGuildConnection>();
        modelBuilder.ApplySoftDeleteFilter<DiscordNotificationConfig>();
        modelBuilder.ApplySoftDeleteFilter<DiscordNotificationRole>();
        modelBuilder.ApplySoftDeleteFilter<SupporterConnection>();
        modelBuilder.ApplySoftDeleteFilter<IntegrationConnection>();
    }

    private static readonly HashSet<Type> MappedEntities =
    [
        typeof(Channel),
        typeof(EventSubSubscription),
        typeof(OutboundWebhookEndpoint),
        typeof(Widget),
        typeof(WidgetVersion),
        typeof(Pipeline),
        typeof(PipelineStep),
        typeof(SoundClip),
        typeof(Reward),
        typeof(Redemption),
        typeof(RedemptionTimer),
        typeof(Giveaway),
        typeof(GiveawayCodePool),
        typeof(GiveawayCode),
        typeof(GiveawayEntry),
        typeof(GiveawayWinner),
        typeof(PickList),
        typeof(ChannelAsset),
        typeof(CustomDataSource),
        typeof(EventResponse),
        typeof(Command),
        typeof(NomNomzBot.Domain.Commands.Entities.Timer),
        typeof(ChatTrigger),
        typeof(CatalogItem),
        typeof(CatalogPurchase),
        typeof(LeaderboardConfig),
        typeof(LeaderboardSnapshot),
        typeof(CodeScript),
        typeof(CodeScriptVersion),
        typeof(InboundWebhookEndpoint),
        typeof(SupporterConnection),
        typeof(InstalledBundle),
        typeof(DiscordGuildConnection),
        typeof(DiscordNotificationConfig),
        typeof(DiscordNotificationRole),
        typeof(IntegrationConnection),
        typeof(Service),
    ];

    /// <summary>
    /// Every <see cref="IApplicationDbContext"/> entity OUTSIDE the counted slice, derived by reflection from
    /// the interface so it never silently drifts when the contract grows.
    /// </summary>
    private static readonly IReadOnlyList<Type> UnmappedEntities =
    [
        .. typeof(IApplicationDbContext)
            .GetProperties()
            .Where(p =>
                p.PropertyType.IsGenericType
                && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
            )
            .Select(p => p.PropertyType.GetGenericArguments()[0])
            .Where(t => !MappedEntities.Contains(t)),
    ];

    // -- Mapped counted slice --
    public DbSet<Widget> Widgets => Set<Widget>();
    public DbSet<WidgetVersion> WidgetVersions => Set<WidgetVersion>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<PlatformConnection> PlatformConnections => Set<PlatformConnection>();
    public DbSet<Pipeline> Pipelines => Set<Pipeline>();
    public DbSet<PipelineStep> PipelineSteps => Set<PipelineStep>();
    public DbSet<SoundClip> SoundClips => Set<SoundClip>();
    public DbSet<Reward> Rewards => Set<Reward>();
    public DbSet<Redemption> Redemptions => Set<Redemption>();
    public DbSet<RedemptionTimer> RedemptionTimers => Set<RedemptionTimer>();
    public DbSet<Giveaway> Giveaways => Set<Giveaway>();
    public DbSet<GiveawayCodePool> GiveawayCodePools => Set<GiveawayCodePool>();
    public DbSet<GiveawayCode> GiveawayCodes => Set<GiveawayCode>();
    public DbSet<User> Users => throw new NotSupportedException();
    public DbSet<WidgetGalleryItem> WidgetGalleryItems => throw new NotSupportedException();
    public DbSet<WidgetGallerySubmissionEvent> WidgetGallerySubmissionEvents =>
        throw new NotSupportedException();
    public DbSet<RenderedAlertCapture> RenderedAlertCaptures => throw new NotSupportedException();

    // ── Unused IApplicationDbContext surface — never reached by these tests ──
    public DbSet<NomNomzBot.Domain.Quotes.Entities.Quote> Quotes =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Music.Entities.BlockedTrack> BlockedTracks =>
        throw new NotSupportedException();
    public DbSet<PickList> PickLists => Set<PickList>();
    public DbSet<ChatTrigger> ChatTriggers => Set<ChatTrigger>();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.ChannelModerationStanding> ChannelModerationStandings =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.SharedBanSettings> SharedBanSettings =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.SharedBanTrustedChannel> SharedBanTrustedChannels =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.NetworkNukeBatch> NetworkNukeBatches =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.UserModerationHistory> UserModerationHistories =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.UserTrustScore> UserTrustScores =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.ModerationEscalationPolicy> ModerationEscalationPolicies =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.ModerationEscalationState> ModerationEscalationStates =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.ChatFilter> ChatFilters =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.ModerationQueueItem> ModerationQueueItems =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Notifications.Entities.ActionRequiredDismissal> ActionRequiredDismissals =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Community.Entities.ChatPoll> ChatPolls =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Community.Entities.ChatPollVote> ChatPollVotes =>
        throw new NotSupportedException();
    public DbSet<TenantSequence> TenantSequences => throw new NotSupportedException();
    public DbSet<UserIdentity> UserIdentities => throw new NotSupportedException();
    public DbSet<ConsentRecord> ConsentRecords => throw new NotSupportedException();
    public DbSet<ErasureRequest> ErasureRequests => throw new NotSupportedException();
    public DbSet<ChannelModerator> ChannelModerators => throw new NotSupportedException();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Command> Commands => Set<Command>();
    public DbSet<EventSubSubscription> EventSubSubscriptions => Set<EventSubSubscription>();
    public DbSet<EventSubConduit> EventSubConduits => throw new NotSupportedException();
    public DbSet<EventSubConduitShard> EventSubConduitShards => throw new NotSupportedException();
    public DbSet<IdempotencyKey> IdempotencyKeys => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Chat.Entities.ChatMessage> ChatMessages =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Chat.Entities.YouTubeLiveChatBan> YouTubeLiveChatBans =>
        throw new NotSupportedException();
    public DbSet<GiveawayEntry> GiveawayEntries => Set<GiveawayEntry>();
    public DbSet<GiveawayWinner> GiveawayWinners => Set<GiveawayWinner>();
    public DbSet<ChannelEvent> ChannelEvents => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Stream.Entities.Stream> Streams =>
        throw new NotSupportedException();
    public DbSet<Configuration> Configurations => throw new NotSupportedException();
    public DbSet<Storage> Storages => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.Record> Records =>
        throw new NotSupportedException();
    public DbSet<Permission> Permissions => throw new NotSupportedException();
    public DbSet<ChannelFeature> ChannelFeatures => throw new NotSupportedException();
    public DbSet<ChannelBotAuthorization> ChannelBotAuthorizations =>
        throw new NotSupportedException();
    public DbSet<BotAccount> BotAccounts => throw new NotSupportedException();
    public DbSet<AuthSession> AuthSessions => throw new NotSupportedException();
    public DbSet<RefreshToken> RefreshTokens => throw new NotSupportedException();
    public DbSet<IpcDevModeKey> IpcDevModeKeys => throw new NotSupportedException();
    public DbSet<IntegrationConnection> IntegrationConnections => Set<IntegrationConnection>();
    public DbSet<IntegrationToken> IntegrationTokens => throw new NotSupportedException();
    public DbSet<CryptoKey> CryptoKeys => throw new NotSupportedException();
    public DbSet<KeyUsageBinding> KeyUsageBindings => throw new NotSupportedException();
    public DbSet<EventSubjectKey> EventSubjectKeys => throw new NotSupportedException();
    public DbSet<DiscordGuildConnection> DiscordGuildConnections => Set<DiscordGuildConnection>();
    public DbSet<DiscordNotificationConfig> DiscordNotificationConfigs =>
        Set<DiscordNotificationConfig>();
    public DbSet<DiscordNotificationRole> DiscordNotificationRoles =>
        Set<DiscordNotificationRole>();
    public DbSet<DiscordMemberOptIn> DiscordMemberOptIns => throw new NotSupportedException();
    public DbSet<DiscordNotificationDispatch> DiscordNotificationDispatches =>
        throw new NotSupportedException();
    public DbSet<DiscordLiveRoleConfig> DiscordLiveRoleConfigs => throw new NotSupportedException();
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
    public DbSet<Pronoun> Pronouns => throw new NotSupportedException();
    public DbSet<DeletionAuditLog> DeletionAuditLogs => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Stream.Entities.ShoutoutOverride> ShoutoutOverrides =>
        throw new NotSupportedException();
    public DbSet<ComplianceAuditLog> ComplianceAuditLogs => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.Timer> Timers =>
        Set<NomNomzBot.Domain.Commands.Entities.Timer>();
    public DbSet<EventResponse> EventResponses => Set<EventResponse>();
    public DbSet<WatchStreak> WatchStreaks => throw new NotSupportedException();
    public DbSet<ScheduledPipelineTask> ScheduledPipelineTasks => throw new NotSupportedException();
    public DbSet<PipelineStepCondition> PipelineStepConditions => throw new NotSupportedException();
    public DbSet<PipelineTrigger> PipelineTriggers => throw new NotSupportedException();
    public DbSet<PipelineExecution> PipelineExecutions => throw new NotSupportedException();
    public DbSet<PipelineRunState> PipelineRunStates => throw new NotSupportedException();
    public DbSet<ChannelBuiltinCommand> ChannelBuiltinCommands => throw new NotSupportedException();
    public DbSet<CommandCooldownState> CommandCooldownStates => throw new NotSupportedException();
    public DbSet<NamedCounter> NamedCounters => throw new NotSupportedException();
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
    public DbSet<CommandUsage> CommandUsages => throw new NotSupportedException();
    public DbSet<EventJournal> EventJournals => throw new NotSupportedException();
    public DbSet<ProjectionCheckpoint> ProjectionCheckpoints => throw new NotSupportedException();
    public DbSet<ChannelMembership> ChannelMemberships => throw new NotSupportedException();
    public DbSet<ChannelCommunityStanding> ChannelCommunityStandings =>
        throw new NotSupportedException();
    public DbSet<ActionDefinition> ActionDefinitions => throw new NotSupportedException();
    public DbSet<ChannelActionOverride> ChannelActionOverrides => throw new NotSupportedException();
    public DbSet<PermitGrant> PermitGrants => throw new NotSupportedException();
    public DbSet<ChannelMissingScope> ChannelMissingScopes => throw new NotSupportedException();
    public DbSet<IamPermission> IamPermissions => throw new NotSupportedException();
    public DbSet<IamRole> IamRoles => throw new NotSupportedException();
    public DbSet<IamRolePermission> IamRolePermissions => throw new NotSupportedException();
    public DbSet<IamPrincipal> IamPrincipals => throw new NotSupportedException();
    public DbSet<IamRoleAssignment> IamRoleAssignments => throw new NotSupportedException();
    public DbSet<SecurityNotice> SecurityNotices => throw new NotSupportedException();
    public DbSet<IamAuditLog> IamAuditLogs => throw new NotSupportedException();
    public DbSet<CurrencyConfig> CurrencyConfigs => throw new NotSupportedException();
    public DbSet<EarningRule> EarningRules => throw new NotSupportedException();
    public DbSet<CurrencyAccount> CurrencyAccounts => throw new NotSupportedException();
    public DbSet<CurrencyLedgerEntry> CurrencyLedgerEntries => throw new NotSupportedException();
    public DbSet<CatalogItem> CatalogItems => Set<CatalogItem>();
    public DbSet<CatalogPurchase> CatalogPurchases => Set<CatalogPurchase>();
    public DbSet<GameConfig> GameConfigs => throw new NotSupportedException();
    public DbSet<GamePlay> GamePlays => throw new NotSupportedException();
    public DbSet<InstalledBundle> InstalledBundles => Set<InstalledBundle>();
    public DbSet<GameSession> GameSessions => throw new NotSupportedException();
    public DbSet<ViewerAgeConsent> ViewerAgeConsents => throw new NotSupportedException();
    public DbSet<SavingsJar> SavingsJars => throw new NotSupportedException();
    public DbSet<SavingsJarMembership> SavingsJarMemberships => throw new NotSupportedException();
    public DbSet<JarContribution> JarContributions => throw new NotSupportedException();
    public DbSet<LeaderboardConfig> LeaderboardConfigs => Set<LeaderboardConfig>();
    public DbSet<LeaderboardOptOut> LeaderboardOptOuts => throw new NotSupportedException();
    public DbSet<LeaderboardSnapshot> LeaderboardSnapshots => Set<LeaderboardSnapshot>();
    public DbSet<NomNomzBot.Domain.Billing.Entities.BillingTier> BillingTiers =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Billing.Entities.TierLimit> TierLimits =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Billing.Entities.Subscription> Subscriptions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Billing.Entities.Invoice> Invoices =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Billing.Entities.UsageRecord> UsageRecords =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Billing.Entities.FoundersBadge> FoundersBadges =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Billing.Entities.InviteCode> InviteCodes =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Federation.Entities.FederationPeer> FederationPeers =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Federation.Entities.FederationPeerKey> FederationPeerKeys =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Federation.Entities.ChannelFederationOptIn> ChannelFederationOptIns =>
        throw new NotSupportedException();
    public DbSet<OutboundWebhookEndpoint> OutboundWebhookEndpoints =>
        Set<OutboundWebhookEndpoint>();
    public DbSet<OutboundWebhookDelivery> OutboundWebhookDeliveries =>
        throw new NotSupportedException();
    public DbSet<InboundWebhookEndpoint> InboundWebhookEndpoints => Set<InboundWebhookEndpoint>();
    public DbSet<HttpEgressAllowlist> HttpEgressAllowlists => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Analytics.Entities.ViewerProfile> ViewerProfiles =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Analytics.Entities.WatchSession> WatchSessions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Analytics.Entities.MessageActivityDaily> MessageActivityDailies =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Analytics.Entities.ViewerEngagementDaily> ViewerEngagementDailies =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Analytics.Entities.ChannelAnalyticsDaily> ChannelAnalyticsDailies =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Analytics.Entities.ChannelChatterDay> ChannelChatterDays =>
        throw new NotSupportedException();
    public DbSet<FeatureFlag> FeatureFlags => throw new NotSupportedException();
    public DbSet<FeatureFlagOverride> FeatureFlagOverrides => throw new NotSupportedException();
    public DbSet<CodeScript> CodeScripts => Set<CodeScript>();
    public DbSet<CodeScriptVersion> CodeScriptVersions => Set<CodeScriptVersion>();
    public DbSet<ChannelAsset> ChannelAssets => Set<ChannelAsset>();
    public DbSet<CustomDataSource> CustomDataSources => Set<CustomDataSource>();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.ViewerReport> ViewerReports =>
        throw new NotSupportedException();
    public DbSet<SupporterConnection> SupporterConnections => Set<SupporterConnection>();
    public DbSet<SupporterEvent> SupporterEvents => throw new NotSupportedException();
}

/// <summary>Opens a fresh, isolated SQLite database (one connection kept open for the test's lifetime).</summary>
internal sealed class BlastRadiusSqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    private BlastRadiusSqliteTestDatabase(SqliteConnection connection) => _connection = connection;

    public static BlastRadiusSqliteTestDatabase Open()
    {
        SqliteConnection connection = new("DataSource=:memory:");
        connection.Open();
        BlastRadiusSqliteTestDatabase db = new(connection);
        using BlastRadiusTestDbContext context = db.NewContext();
        context.Database.EnsureCreated();
        return db;
    }

    public BlastRadiusTestDbContext NewContext()
    {
        DbContextOptions<BlastRadiusTestDbContext> options =
            new DbContextOptionsBuilder<BlastRadiusTestDbContext>()
                .UseSqlite(_connection)
                .AddInterceptors(
                    new SoftDeleteInterceptor(
                        TimeProvider.System,
                        new NomNomzBot.Infrastructure.Tests.Platform.Persistence.NullCurrentUserService()
                    )
                )
                .Options;
        return new(options);
    }

    public void Dispose() => _connection.Dispose();
}
