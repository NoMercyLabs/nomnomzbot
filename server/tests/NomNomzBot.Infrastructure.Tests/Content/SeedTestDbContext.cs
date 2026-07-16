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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using NomNomzBot.Infrastructure.Commands.Persistence;
using NomNomzBot.Infrastructure.Identity.Persistence;
using NomNomzBot.Infrastructure.Platform.Persistence.Configurations;
using NomNomzBot.Infrastructure.Tts.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Content;

/// <summary>
/// A focused <see cref="IApplicationDbContext"/> for the seed tests. The production
/// <see cref="NomNomzBot.Infrastructure.Platform.Persistence.AppDbContext"/> is hard-bound to
/// Npgsql's <c>jsonb</c> mapper for complex-typed columns (e.g. <c>ChatMessage.Badges</c>), which
/// neither the InMemory nor the SQLite provider can materialize — so it cannot be hosted on a
/// test provider. This context maps ONLY the five entities the seeders touch, applying their REAL
/// EF configurations (relational-only annotations like <c>jsonb</c>/<c>HasDefaultValueSql</c> are
/// no-ops on InMemory; <c>List&lt;string&gt;</c> and string columns map natively), and ignores
/// every other entity so EF never reaches an unmappable jsonb-of-complex-type column. The seeders
/// thus run against the real schema for the rows they write, on a real EF provider.
/// </summary>
public sealed class SeedTestDbContext : DbContext, IApplicationDbContext
{
    public SeedTestDbContext(DbContextOptions<SeedTestDbContext> options)
        : base(options) { }

    // ── Entities the seeders touch ───────────────────────────────────────────
    public DbSet<TtsVoice> TtsVoices => Set<TtsVoice>();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.Redemption> Redemptions =>
        Set<NomNomzBot.Domain.Rewards.Entities.Redemption>();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.RedemptionTimer> RedemptionTimers =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.ChatTrigger> ChatTriggers =>
        throw new NotSupportedException();
    public DbSet<Pronoun> Pronouns => Set<Pronoun>();
    public DbSet<Configuration> Configurations => Set<Configuration>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Command> Commands => Set<Command>();

    // ── Remaining IApplicationDbContext surface (not mapped — Ignored below) ──
    public DbSet<User> Users => Set<User>();
    public DbSet<UserIdentity> UserIdentities => Set<UserIdentity>();
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
    public DbSet<ChannelModerator> ChannelModerators => Set<ChannelModerator>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<Reward> Rewards => Set<Reward>();
    public DbSet<NomNomzBot.Domain.Quotes.Entities.Quote> Quotes =>
        Set<NomNomzBot.Domain.Quotes.Entities.Quote>();
    public DbSet<NomNomzBot.Domain.PickLists.Entities.PickList> PickLists =>
        throw new NotSupportedException();
    public DbSet<Widget> Widgets => Set<Widget>();
    public DbSet<NomNomzBot.Domain.Widgets.Entities.WidgetVersion> WidgetVersions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Widgets.Entities.WidgetGalleryItem> WidgetGalleryItems =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Widgets.Entities.WidgetGallerySubmissionEvent> WidgetGallerySubmissionEvents =>
        throw new NotSupportedException();
    public DbSet<EventSubSubscription> EventSubSubscriptions => Set<EventSubSubscription>();
    public DbSet<EventSubConduit> EventSubConduits => Set<EventSubConduit>();
    public DbSet<EventSubConduitShard> EventSubConduitShards => Set<EventSubConduitShard>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<YouTubeLiveChatBan> YouTubeLiveChatBans => Set<YouTubeLiveChatBan>();
    public DbSet<NomNomzBot.Domain.Giveaways.Entities.Giveaway> Giveaways =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Giveaways.Entities.GiveawayEntry> GiveawayEntries =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Giveaways.Entities.GiveawayWinner> GiveawayWinners =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Giveaways.Entities.GiveawayCodePool> GiveawayCodePools =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Giveaways.Entities.GiveawayCode> GiveawayCodes =>
        throw new NotSupportedException();
    public DbSet<ChannelEvent> ChannelEvents => Set<ChannelEvent>();
    public DbSet<global::NomNomzBot.Domain.Stream.Entities.Stream> Streams =>
        Set<global::NomNomzBot.Domain.Stream.Entities.Stream>();
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
    public DbSet<NomNomzBot.Domain.Integrations.Entities.IntegrationConnection> IntegrationConnections =>
        Set<NomNomzBot.Domain.Integrations.Entities.IntegrationConnection>();
    public DbSet<NomNomzBot.Domain.Integrations.Entities.IntegrationToken> IntegrationTokens =>
        Set<NomNomzBot.Domain.Integrations.Entities.IntegrationToken>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.CryptoKey> CryptoKeys =>
        Set<NomNomzBot.Domain.Identity.Entities.CryptoKey>();
    public DbSet<DiscordGuildConnection> DiscordGuildConnections => Set<DiscordGuildConnection>();
    public DbSet<DiscordNotificationConfig> DiscordNotificationConfigs =>
        Set<DiscordNotificationConfig>();
    public DbSet<DiscordNotificationRole> DiscordNotificationRoles =>
        Set<DiscordNotificationRole>();
    public DbSet<DiscordMemberOptIn> DiscordMemberOptIns => Set<DiscordMemberOptIn>();
    public DbSet<DiscordNotificationDispatch> DiscordNotificationDispatches =>
        Set<DiscordNotificationDispatch>();
    public DbSet<ChannelSubscription> ChannelSubscriptions => Set<ChannelSubscription>();
    public DbSet<UserTtsVoice> UserTtsVoices => Set<UserTtsVoice>();
    public DbSet<TtsUsageRecord> TtsUsageRecords => Set<TtsUsageRecord>();
    public DbSet<TtsCacheEntry> TtsCacheEntries => Set<TtsCacheEntry>();
    public DbSet<TtsApprovalQueueEntry> TtsApprovalQueueEntries => Set<TtsApprovalQueueEntry>();
    public DbSet<DeletionAuditLog> DeletionAuditLogs => Set<DeletionAuditLog>();
    public DbSet<NomNomzBot.Domain.Commands.Entities.Timer> Timers =>
        Set<NomNomzBot.Domain.Commands.Entities.Timer>();
    public DbSet<EventResponse> EventResponses => Set<EventResponse>();
    public DbSet<WatchStreak> WatchStreaks => Set<WatchStreak>();
    public DbSet<Pipeline> Pipelines => Set<Pipeline>();
    public DbSet<PipelineStep> PipelineSteps => Set<PipelineStep>();
    public DbSet<PipelineStepCondition> PipelineStepConditions => Set<PipelineStepCondition>();
    public DbSet<PipelineExecution> PipelineExecutions => Set<PipelineExecution>();
    public DbSet<ChannelBuiltinCommand> ChannelBuiltinCommands => Set<ChannelBuiltinCommand>();
    public DbSet<CommandCooldownState> CommandCooldownStates => Set<CommandCooldownState>();
    public DbSet<NamedCounter> NamedCounters => Set<NamedCounter>();
    public DbSet<NomNomzBot.Domain.ViewerData.Entities.ViewerDatum> ViewerData =>
        Set<NomNomzBot.Domain.ViewerData.Entities.ViewerDatum>();
    public DbSet<NomNomzBot.Domain.Engagement.Entities.EngagementConfig> EngagementConfigs =>
        Set<NomNomzBot.Domain.Engagement.Entities.EngagementConfig>();
    public DbSet<NomNomzBot.Domain.Engagement.Entities.ViewerEngagementState> ViewerEngagementStates =>
        Set<NomNomzBot.Domain.Engagement.Entities.ViewerEngagementState>();
    public DbSet<NomNomzBot.Domain.MediaShare.Entities.MediaShareConfig> MediaShareConfigs =>
        Set<NomNomzBot.Domain.MediaShare.Entities.MediaShareConfig>();
    public DbSet<NomNomzBot.Domain.MediaShare.Entities.MediaShareRequest> MediaShareRequests =>
        Set<NomNomzBot.Domain.MediaShare.Entities.MediaShareRequest>();
    public DbSet<CommandUsage> CommandUsages => Set<CommandUsage>();
    public DbSet<NomNomzBot.Domain.EventStore.Entities.EventJournal> EventJournals =>
        Set<NomNomzBot.Domain.EventStore.Entities.EventJournal>();
    public DbSet<NomNomzBot.Domain.EventStore.Entities.TenantSequence> TenantSequences =>
        Set<NomNomzBot.Domain.EventStore.Entities.TenantSequence>();
    public DbSet<NomNomzBot.Domain.EventStore.Entities.ProjectionCheckpoint> ProjectionCheckpoints =>
        Set<NomNomzBot.Domain.EventStore.Entities.ProjectionCheckpoint>();
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
        throw new NotSupportedException();
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
        Set<NomNomzBot.Domain.Sound.Entities.SoundClip>();
    public DbSet<NomNomzBot.Domain.CustomEvents.Entities.CustomDataSource> CustomDataSources =>
        Set<NomNomzBot.Domain.CustomEvents.Entities.CustomDataSource>();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.ViewerReport> ViewerReports =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Supporters.Entities.SupporterConnection> SupporterConnections =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Supporters.Entities.SupporterEvent> SupporterEvents =>
        throw new NotSupportedException();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Only the five entities under test are mapped, with their real configurations.
        modelBuilder.ApplyConfiguration(new TtsVoiceConfiguration());
        modelBuilder.ApplyConfiguration(new PronounConfiguration());
        modelBuilder.ApplyConfiguration(new ConfigurationEntityConfiguration());
        modelBuilder.ApplyConfiguration(new ChannelConfiguration());
        modelBuilder.ApplyConfiguration(new CommandConfiguration());

        // Every entity NOT under test is ignored — EF would otherwise auto-discover them from
        // the DbSet<T> properties (an IApplicationDbContext requirement) and try to map their
        // jsonb-of-complex-type columns, which no test provider supports.
        modelBuilder.Ignore<User>();
        modelBuilder.Ignore<ChannelModerator>();
        modelBuilder.Ignore<Service>();
        modelBuilder.Ignore<Reward>();
        modelBuilder.Ignore<NomNomzBot.Domain.Quotes.Entities.Quote>();
        modelBuilder.Ignore<NomNomzBot.Domain.PickLists.Entities.PickList>();
        modelBuilder.Ignore<Widget>();
        modelBuilder.Ignore<NomNomzBot.Domain.Widgets.Entities.WidgetVersion>();
        modelBuilder.Ignore<NomNomzBot.Domain.Widgets.Entities.WidgetGalleryItem>();
        modelBuilder.Ignore<NomNomzBot.Domain.Widgets.Entities.WidgetGallerySubmissionEvent>();
        modelBuilder.Ignore<EventSubSubscription>();
        modelBuilder.Ignore<EventSubConduit>();
        modelBuilder.Ignore<EventSubConduitShard>();
        modelBuilder.Ignore<IdempotencyKey>();
        modelBuilder.Ignore<ChatMessage>();
        modelBuilder.Ignore<ChannelEvent>();
        modelBuilder.Ignore<global::NomNomzBot.Domain.Stream.Entities.Stream>();
        modelBuilder.Ignore<Storage>();
        modelBuilder.Ignore<NomNomzBot.Domain.Platform.Entities.Record>();
        modelBuilder.Ignore<Permission>();
        modelBuilder.Ignore<ChannelFeature>();
        modelBuilder.Ignore<ChannelBotAuthorization>();
        modelBuilder.Ignore<BotAccount>();
        modelBuilder.Ignore<AuthSession>();
        modelBuilder.Ignore<RefreshToken>();
        modelBuilder.Ignore<IpcDevModeKey>();
        modelBuilder.Ignore<NomNomzBot.Domain.Integrations.Entities.IntegrationConnection>();
        modelBuilder.Ignore<NomNomzBot.Domain.Integrations.Entities.IntegrationToken>();
        modelBuilder.Ignore<DiscordGuildConnection>();
        modelBuilder.Ignore<DiscordNotificationConfig>();
        modelBuilder.Ignore<DiscordNotificationRole>();
        modelBuilder.Ignore<DiscordMemberOptIn>();
        modelBuilder.Ignore<DiscordNotificationDispatch>();
        modelBuilder.Ignore<ChannelSubscription>();
        modelBuilder.Ignore<UserTtsVoice>();
        modelBuilder.Ignore<TtsUsageRecord>();
        modelBuilder.Ignore<TtsCacheEntry>();
        modelBuilder.Ignore<DeletionAuditLog>();
        modelBuilder.Ignore<NomNomzBot.Domain.Commands.Entities.Timer>();
        modelBuilder.Ignore<EventResponse>();
        modelBuilder.Ignore<WatchStreak>();
        modelBuilder.Ignore<Pipeline>();
        modelBuilder.Ignore<PipelineStep>();
        modelBuilder.Ignore<PipelineStepCondition>();
        modelBuilder.Ignore<PipelineExecution>();
        modelBuilder.Ignore<CommandCooldownState>();
        modelBuilder.Ignore<NamedCounter>();
        modelBuilder.Ignore<CommandUsage>();
        modelBuilder.Ignore<NomNomzBot.Domain.EventStore.Entities.EventJournal>();
        modelBuilder.Ignore<NomNomzBot.Domain.EventStore.Entities.TenantSequence>();
        modelBuilder.Ignore<NomNomzBot.Domain.EventStore.Entities.ProjectionCheckpoint>();
        modelBuilder.Ignore<NomNomzBot.Domain.Sound.Entities.SoundClip>();
    }
}
