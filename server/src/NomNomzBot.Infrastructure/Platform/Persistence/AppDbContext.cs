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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Platform.Persistence.Extensions;

namespace NomNomzBot.Infrastructure.Platform.Persistence;

public class AppDbContext : DbContext, IApplicationDbContext
{
    private readonly ICurrentTenantService? _currentTenant;

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenantService currentTenant)
        : base(options)
    {
        _currentTenant = currentTenant;
    }

    // Read by the named tenant query filter at query time (schema §1.2). Null tenant ⇒ no tenant
    // predicate (background / cross-tenant reads see all rows; soft-delete still applies).
    private Guid? CurrentBroadcasterId => _currentTenant?.BroadcasterId;

    // Core
    public DbSet<User> Users => Set<User>();
    public DbSet<UserIdentity> UserIdentities => Set<UserIdentity>();
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<ChannelModerator> ChannelModerators => Set<ChannelModerator>();
    public DbSet<Service> Services => Set<Service>();

    // Bot features
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
    public DbSet<NomNomzBot.Domain.Community.Entities.ChatPoll> ChatPolls =>
        Set<NomNomzBot.Domain.Community.Entities.ChatPoll>();
    public DbSet<NomNomzBot.Domain.Community.Entities.ChatPollVote> ChatPollVotes =>
        Set<NomNomzBot.Domain.Community.Entities.ChatPollVote>();
    public DbSet<Domain.Quotes.Entities.Quote> Quotes => Set<Domain.Quotes.Entities.Quote>();

    // Generic named pick-lists (the {list.pick.<name>} primitive).
    public DbSet<Domain.PickLists.Entities.PickList> PickLists =>
        Set<Domain.PickLists.Entities.PickList>();

    // Giveaways (giveaways.md G.6–G.10): campaign, entries, append-only winner history, secret-safe
    // code pools + AEAD-encrypted codes.
    public DbSet<Domain.Giveaways.Entities.Giveaway> Giveaways =>
        Set<Domain.Giveaways.Entities.Giveaway>();
    public DbSet<Domain.Giveaways.Entities.GiveawayEntry> GiveawayEntries =>
        Set<Domain.Giveaways.Entities.GiveawayEntry>();
    public DbSet<Domain.Giveaways.Entities.GiveawayWinner> GiveawayWinners =>
        Set<Domain.Giveaways.Entities.GiveawayWinner>();
    public DbSet<Domain.Giveaways.Entities.GiveawayCodePool> GiveawayCodePools =>
        Set<Domain.Giveaways.Entities.GiveawayCodePool>();
    public DbSet<Domain.Giveaways.Entities.GiveawayCode> GiveawayCodes =>
        Set<Domain.Giveaways.Entities.GiveawayCode>();
    public DbSet<Widget> Widgets => Set<Widget>();

    // EventSub — per-tenant subscription registry (F.7), app-global conduit + shards (F.8/F.9),
    // and the scoped idempotency markers the notification dispatcher dedupes on (O.4).
    public DbSet<EventSubSubscription> EventSubSubscriptions => Set<EventSubSubscription>();
    public DbSet<EventSubConduit> EventSubConduits => Set<EventSubConduit>();
    public DbSet<EventSubConduitShard> EventSubConduitShards => Set<EventSubConduitShard>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

    // Chat
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<YouTubeLiveChatBan> YouTubeLiveChatBans => Set<YouTubeLiveChatBan>();
    public DbSet<ChannelEvent> ChannelEvents => Set<ChannelEvent>();
    public DbSet<Domain.Stream.Entities.Stream> Streams => Set<Domain.Stream.Entities.Stream>();

    // Config & Storage
    public DbSet<Domain.Platform.Entities.Configuration> Configurations =>
        Set<Domain.Platform.Entities.Configuration>();
    public DbSet<Storage> Storages => Set<Storage>();
    public DbSet<Record> Records => Set<Record>();

    // Permissions
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<ChannelFeature> ChannelFeatures => Set<ChannelFeature>();

    // Auth & Billing
    public DbSet<ChannelBotAuthorization> ChannelBotAuthorizations =>
        Set<ChannelBotAuthorization>();
    public DbSet<BotAccount> BotAccounts => Set<BotAccount>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<IpcDevModeKey> IpcDevModeKeys => Set<IpcDevModeKey>();
    public DbSet<DiscordGuildConnection> DiscordGuildConnections => Set<DiscordGuildConnection>();
    public DbSet<DiscordNotificationConfig> DiscordNotificationConfigs =>
        Set<DiscordNotificationConfig>();
    public DbSet<DiscordNotificationRole> DiscordNotificationRoles =>
        Set<DiscordNotificationRole>();
    public DbSet<DiscordMemberOptIn> DiscordMemberOptIns => Set<DiscordMemberOptIn>();
    public DbSet<DiscordNotificationDispatch> DiscordNotificationDispatches =>
        Set<DiscordNotificationDispatch>();
    public DbSet<ChannelSubscription> ChannelSubscriptions => Set<ChannelSubscription>();

    // Integrations (token vault — identity-auth Domain E)
    public DbSet<IntegrationConnection> IntegrationConnections => Set<IntegrationConnection>();
    public DbSet<IntegrationToken> IntegrationTokens => Set<IntegrationToken>();

    // DEK registry (schema Q.1) — wrapped per-subject/tenant DEKs; the crypto-shred + restart-survival linchpin.
    public DbSet<CryptoKey> CryptoKeys => Set<CryptoKey>();

    // Automation API (external tokens — automation-api.md P.17)
    public DbSet<NomNomzBot.Domain.Automation.Entities.AutomationApiToken> AutomationApiTokens =>
        Set<NomNomzBot.Domain.Automation.Entities.AutomationApiToken>();

    // OBS control (per-channel connection config — obs-control.md P.14)
    public DbSet<NomNomzBot.Domain.Obs.Entities.ObsConnection> ObsConnections =>
        Set<NomNomzBot.Domain.Obs.Entities.ObsConnection>();

    // VTube Studio (per-channel connection config — vtube-studio.md P.19)
    public DbSet<NomNomzBot.Domain.Vts.Entities.VtsConnection> VtsConnections =>
        Set<NomNomzBot.Domain.Vts.Entities.VtsConnection>();

    // TTS
    public DbSet<TtsConfig> TtsConfigs => Set<TtsConfig>();
    public DbSet<TtsVoice> TtsVoices => Set<TtsVoice>();
    public DbSet<UserTtsVoice> UserTtsVoices => Set<UserTtsVoice>();
    public DbSet<TtsUsageRecord> TtsUsageRecords => Set<TtsUsageRecord>();
    public DbSet<TtsCacheEntry> TtsCacheEntries => Set<TtsCacheEntry>();
    public DbSet<TtsApprovalQueueEntry> TtsApprovalQueueEntries => Set<TtsApprovalQueueEntry>();

    // Reference data
    public DbSet<Pronoun> Pronouns => Set<Pronoun>();

    // Audit
    public DbSet<DeletionAuditLog> DeletionAuditLogs => Set<DeletionAuditLog>();

    // Timers
    public DbSet<Domain.Commands.Entities.Timer> Timers => Set<Domain.Commands.Entities.Timer>();

    // Event responses
    public DbSet<EventResponse> EventResponses => Set<EventResponse>();

    // Watch streaks
    public DbSet<WatchStreak> WatchStreaks => Set<WatchStreak>();

    // Pipelines + steps + telemetry
    public DbSet<Domain.Commands.Entities.Pipeline> Pipelines =>
        Set<Domain.Commands.Entities.Pipeline>();

    public DbSet<PipelineStep> PipelineSteps => Set<PipelineStep>();
    public DbSet<PipelineStepCondition> PipelineStepConditions => Set<PipelineStepCondition>();
    public DbSet<PipelineExecution> PipelineExecutions => Set<PipelineExecution>();
    public DbSet<ChannelBuiltinCommand> ChannelBuiltinCommands => Set<ChannelBuiltinCommand>();
    public DbSet<CommandCooldownState> CommandCooldownStates => Set<CommandCooldownState>();
    public DbSet<NamedCounter> NamedCounters => Set<NamedCounter>();
    public DbSet<Domain.ViewerData.Entities.ViewerDatum> ViewerData =>
        Set<Domain.ViewerData.Entities.ViewerDatum>();
    public DbSet<Domain.Engagement.Entities.EngagementConfig> EngagementConfigs =>
        Set<Domain.Engagement.Entities.EngagementConfig>();
    public DbSet<Domain.Moderation.Entities.ViewerReport> ViewerReports =>
        Set<Domain.Moderation.Entities.ViewerReport>();
    public DbSet<Domain.Engagement.Entities.ViewerEngagementState> ViewerEngagementStates =>
        Set<Domain.Engagement.Entities.ViewerEngagementState>();
    public DbSet<Domain.MediaShare.Entities.MediaShareConfig> MediaShareConfigs =>
        Set<Domain.MediaShare.Entities.MediaShareConfig>();
    public DbSet<Domain.MediaShare.Entities.MediaShareRequest> MediaShareRequests =>
        Set<Domain.MediaShare.Entities.MediaShareRequest>();
    public DbSet<Domain.Supporters.Entities.SupporterConnection> SupporterConnections =>
        Set<Domain.Supporters.Entities.SupporterConnection>();
    public DbSet<Domain.Supporters.Entities.SupporterEvent> SupporterEvents =>
        Set<Domain.Supporters.Entities.SupporterEvent>();
    public DbSet<CommandUsage> CommandUsages => Set<CommandUsage>();

    // Event store — append-only journal (O.1), per-tenant sequences (Q.3), projection checkpoints (O.3)
    public DbSet<EventJournal> EventJournals => Set<EventJournal>();
    public DbSet<TenantSequence> TenantSequences => Set<TenantSequence>();
    public DbSet<ProjectionCheckpoint> ProjectionCheckpoints => Set<ProjectionCheckpoint>();

    // Roles & permissions (Plane A/B)
    public DbSet<ChannelMembership> ChannelMemberships => Set<ChannelMembership>();
    public DbSet<ChannelCommunityStanding> ChannelCommunityStandings =>
        Set<ChannelCommunityStanding>();
    public DbSet<ActionDefinition> ActionDefinitions => Set<ActionDefinition>();
    public DbSet<ChannelActionOverride> ChannelActionOverrides => Set<ChannelActionOverride>();
    public DbSet<PermitGrant> PermitGrants => Set<PermitGrant>();
    public DbSet<ChannelMissingScope> ChannelMissingScopes => Set<ChannelMissingScope>();

    // Platform IAM (Plane C)
    public DbSet<IamPermission> IamPermissions => Set<IamPermission>();
    public DbSet<IamRole> IamRoles => Set<IamRole>();
    public DbSet<IamRolePermission> IamRolePermissions => Set<IamRolePermission>();
    public DbSet<IamPrincipal> IamPrincipals => Set<IamPrincipal>();
    public DbSet<IamRoleAssignment> IamRoleAssignments => Set<IamRoleAssignment>();
    public DbSet<IamAuditLog> IamAuditLogs => Set<IamAuditLog>();

    // Economy — currency core (economy.md K.1-K.3)
    public DbSet<Domain.Economy.Entities.CurrencyConfig> CurrencyConfigs =>
        Set<Domain.Economy.Entities.CurrencyConfig>();
    public DbSet<Domain.Economy.Entities.EarningRule> EarningRules =>
        Set<Domain.Economy.Entities.EarningRule>();
    public DbSet<Domain.Economy.Entities.CurrencyAccount> CurrencyAccounts =>
        Set<Domain.Economy.Entities.CurrencyAccount>();
    public DbSet<Domain.Economy.Entities.CurrencyLedgerEntry> CurrencyLedgerEntries =>
        Set<Domain.Economy.Entities.CurrencyLedgerEntry>();
    public DbSet<Domain.Economy.Entities.CatalogItem> CatalogItems =>
        Set<Domain.Economy.Entities.CatalogItem>();
    public DbSet<Domain.Economy.Entities.CatalogPurchase> CatalogPurchases =>
        Set<Domain.Economy.Entities.CatalogPurchase>();
    public DbSet<Domain.Economy.Entities.GameConfig> GameConfigs =>
        Set<Domain.Economy.Entities.GameConfig>();
    public DbSet<Domain.Economy.Entities.GamePlay> GamePlays =>
        Set<Domain.Economy.Entities.GamePlay>();
    public DbSet<Domain.Economy.Entities.GameSession> GameSessions =>
        Set<Domain.Economy.Entities.GameSession>();
    public DbSet<Domain.Economy.Entities.ViewerAgeConsent> ViewerAgeConsents =>
        Set<Domain.Economy.Entities.ViewerAgeConsent>();
    public DbSet<Domain.Economy.Entities.SavingsJar> SavingsJars =>
        Set<Domain.Economy.Entities.SavingsJar>();
    public DbSet<Domain.Economy.Entities.SavingsJarMembership> SavingsJarMemberships =>
        Set<Domain.Economy.Entities.SavingsJarMembership>();
    public DbSet<Domain.Economy.Entities.JarContribution> JarContributions =>
        Set<Domain.Economy.Entities.JarContribution>();
    public DbSet<Domain.Economy.Entities.LeaderboardConfig> LeaderboardConfigs =>
        Set<Domain.Economy.Entities.LeaderboardConfig>();
    public DbSet<Domain.Economy.Entities.LeaderboardOptOut> LeaderboardOptOuts =>
        Set<Domain.Economy.Entities.LeaderboardOptOut>();
    public DbSet<Domain.Economy.Entities.LeaderboardSnapshot> LeaderboardSnapshots =>
        Set<Domain.Economy.Entities.LeaderboardSnapshot>();
    public DbSet<Domain.Billing.Entities.BillingTier> BillingTiers =>
        Set<Domain.Billing.Entities.BillingTier>();
    public DbSet<Domain.Billing.Entities.TierLimit> TierLimits =>
        Set<Domain.Billing.Entities.TierLimit>();
    public DbSet<Domain.Billing.Entities.Subscription> Subscriptions =>
        Set<Domain.Billing.Entities.Subscription>();
    public DbSet<Domain.Billing.Entities.Invoice> Invoices =>
        Set<Domain.Billing.Entities.Invoice>();
    public DbSet<Domain.Billing.Entities.UsageRecord> UsageRecords =>
        Set<Domain.Billing.Entities.UsageRecord>();
    public DbSet<Domain.Billing.Entities.FoundersBadge> FoundersBadges =>
        Set<Domain.Billing.Entities.FoundersBadge>();
    public DbSet<Domain.Billing.Entities.InviteCode> InviteCodes =>
        Set<Domain.Billing.Entities.InviteCode>();
    public DbSet<Domain.Federation.Entities.FederationPeer> FederationPeers =>
        Set<Domain.Federation.Entities.FederationPeer>();
    public DbSet<Domain.Federation.Entities.FederationPeerKey> FederationPeerKeys =>
        Set<Domain.Federation.Entities.FederationPeerKey>();
    public DbSet<Domain.Federation.Entities.ChannelFederationOptIn> ChannelFederationOptIns =>
        Set<Domain.Federation.Entities.ChannelFederationOptIn>();
    public DbSet<Domain.Webhooks.Entities.OutboundWebhookEndpoint> OutboundWebhookEndpoints =>
        Set<Domain.Webhooks.Entities.OutboundWebhookEndpoint>();
    public DbSet<Domain.Webhooks.Entities.OutboundWebhookDelivery> OutboundWebhookDeliveries =>
        Set<Domain.Webhooks.Entities.OutboundWebhookDelivery>();
    public DbSet<Domain.Webhooks.Entities.InboundWebhookEndpoint> InboundWebhookEndpoints =>
        Set<Domain.Webhooks.Entities.InboundWebhookEndpoint>();
    public DbSet<HttpEgressAllowlist> HttpEgressAllowlists => Set<HttpEgressAllowlist>();
    public DbSet<Domain.Analytics.Entities.ViewerProfile> ViewerProfiles =>
        Set<Domain.Analytics.Entities.ViewerProfile>();
    public DbSet<Domain.Analytics.Entities.WatchSession> WatchSessions =>
        Set<Domain.Analytics.Entities.WatchSession>();
    public DbSet<Domain.Analytics.Entities.MessageActivityDaily> MessageActivityDailies =>
        Set<Domain.Analytics.Entities.MessageActivityDaily>();
    public DbSet<Domain.Analytics.Entities.ViewerEngagementDaily> ViewerEngagementDailies =>
        Set<Domain.Analytics.Entities.ViewerEngagementDaily>();
    public DbSet<Domain.Analytics.Entities.ChannelAnalyticsDaily> ChannelAnalyticsDailies =>
        Set<Domain.Analytics.Entities.ChannelAnalyticsDaily>();
    public DbSet<Domain.Analytics.Entities.ChannelChatterDay> ChannelChatterDays =>
        Set<Domain.Analytics.Entities.ChannelChatterDay>();
    public DbSet<DeploymentProfile> DeploymentProfiles => Set<DeploymentProfile>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<FeatureFlagOverride> FeatureFlagOverrides => Set<FeatureFlagOverride>();
    public DbSet<Domain.CustomCode.Entities.CodeScript> CodeScripts =>
        Set<Domain.CustomCode.Entities.CodeScript>();
    public DbSet<Domain.CustomCode.Entities.CodeScriptVersion> CodeScriptVersions =>
        Set<Domain.CustomCode.Entities.CodeScriptVersion>();
    public DbSet<Domain.Sound.Entities.SoundClip> SoundClips =>
        Set<Domain.Sound.Entities.SoundClip>();
    public DbSet<Domain.CustomEvents.Entities.CustomDataSource> CustomDataSources =>
        Set<Domain.CustomEvents.Entities.CustomDataSource>();
    public DbSet<WidgetVersion> WidgetVersions => Set<WidgetVersion>();
    public DbSet<WidgetGalleryItem> WidgetGalleryItems => Set<WidgetGalleryItem>();
    public DbSet<WidgetGallerySubmissionEvent> WidgetGallerySubmissionEvents =>
        Set<WidgetGallerySubmissionEvent>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // On SQLite (the lite profile), pre-claim the model's complex collection/dictionary CLR types as scalar
        // JSON BEFORE relationship discovery runs, or EF treats them as navigations to owned entities and fails.
        // On Postgres the Npgsql provider already maps them (jsonb/hstore) — this is never invoked there.
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            configurationBuilder.ConfigureSqliteJsonConventions();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        bool isSqlite = Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        // On SQLite, pre-empt relationship discovery for Dictionary<string,object> members (Widget.Settings) by
        // mapping them as scalar JSON before configs/finalization. Must precede ApplyConfigurationsFromAssembly.
        if (isSqlite)
            modelBuilder.ApplyScalarJsonProperties();

        // Apply all IEntityTypeConfiguration<T> from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // One provider-agnostic model, two providers (deployment-profile). The model is authored Postgres-first;
        // on SQLite (the lite profile) the Npgsql-native column shapes (jsonb / hstore / text[]) and CLR
        // collection/dictionary properties are rewritten to a portable TEXT-as-JSON mapping so the same model
        // migrates and runs on SQLite. No-op on Postgres.
        if (isSqlite)
            modelBuilder.ApplySqliteCompatibility();

        // Composing tenant + soft-delete global query filters (schema §1.2). Applied after the
        // per-entity configurations so it is the single authoritative filter per entity; the
        // configurations themselves no longer call HasQueryFilter.
        modelBuilder.ApplyTenantAndSoftDeleteFilters(() => CurrentBroadcasterId);
    }
}
