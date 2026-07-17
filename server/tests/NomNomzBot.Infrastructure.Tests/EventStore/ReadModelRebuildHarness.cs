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
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.EventStore.Persistence;
using NomNomzBot.Infrastructure.Platform.Persistence.Configurations;

namespace NomNomzBot.Infrastructure.Tests.EventStore;

/// <summary>
/// A single SQLite-backed <see cref="IApplicationDbContext"/> that hosts BOTH the event journal (its real EF
/// configuration, so the unique <c>(BroadcasterId, StreamPosition)</c> constraint is enforced) AND the whole
/// analytics read model — the five projections' tables (<see cref="ViewerProfile"/>, <see cref="WatchSession"/>,
/// <see cref="MessageActivityDaily"/>, <see cref="ViewerEngagementDaily"/>, <see cref="ChannelAnalyticsDaily"/>)
/// plus the <see cref="User"/> table the viewer resolver get-or-creates into. The production <c>AppDbContext</c>
/// is Npgsql-bound (jsonb-of-complex-type) and the existing event-store / auth test contexts each map only one
/// half; the full reset→replay rebuild proof needs the journal and every read model in one relational store, which
/// is exactly what this maps (and only this — everything else throws, like the other focused test contexts).
/// </summary>
internal sealed class ReadModelRebuildDbContext : DbContext, IApplicationDbContext
{
    public ReadModelRebuildDbContext(DbContextOptions<ReadModelRebuildDbContext> options)
        : base(options) { }

    // ── Journal + the five read models + Users (the only mapped tables) ──
    public DbSet<EventJournal> EventJournals => Set<EventJournal>();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.Redemption> Redemptions =>
        Set<NomNomzBot.Domain.Rewards.Entities.Redemption>();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.RedemptionTimer> RedemptionTimers =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.ChatTrigger> ChatTriggers =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.ChannelModerationStanding> ChannelModerationStandings =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.SharedBanSettings> SharedBanSettings =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.SharedBanTrustedChannel> SharedBanTrustedChannels =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Community.Entities.ChatPoll> ChatPolls =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Community.Entities.ChatPollVote> ChatPollVotes =>
        throw new NotSupportedException();
    public DbSet<TenantSequence> TenantSequences => Set<TenantSequence>();
    public DbSet<ProjectionCheckpoint> ProjectionCheckpoints => Set<ProjectionCheckpoint>();
    public DbSet<ViewerProfile> ViewerProfiles => Set<ViewerProfile>();
    public DbSet<WatchSession> WatchSessions => Set<WatchSession>();
    public DbSet<MessageActivityDaily> MessageActivityDailies => Set<MessageActivityDaily>();
    public DbSet<ViewerEngagementDaily> ViewerEngagementDailies => Set<ViewerEngagementDaily>();
    public DbSet<ChannelAnalyticsDaily> ChannelAnalyticsDailies => Set<ChannelAnalyticsDaily>();
    public DbSet<ChannelChatterDay> ChannelChatterDays => Set<ChannelChatterDay>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserIdentity> UserIdentities => Set<UserIdentity>();
    public DbSet<ChannelEvent> ChannelEvents => Set<ChannelEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new EventJournalConfiguration());
        modelBuilder.ApplyConfiguration(new TenantSequenceConfiguration());
        modelBuilder.ApplyConfiguration(new ProjectionCheckpointConfiguration());
        modelBuilder.ApplyConfiguration(new ViewerProfileConfiguration());
        modelBuilder.ApplyConfiguration(new WatchSessionConfiguration());
        modelBuilder.ApplyConfiguration(new MessageActivityDailyConfiguration());
        modelBuilder.ApplyConfiguration(new ViewerEngagementDailyConfiguration());
        modelBuilder.ApplyConfiguration(new ChannelAnalyticsDailyConfiguration());

        // User is mapped minimally — only the scalar columns the viewer resolver writes; its Channel/Pronoun navs
        // point at entities this context does not host, so drop them (and ignore those entities entirely).
        modelBuilder.Entity<User>(b =>
        {
            b.HasKey(u => u.Id);
            b.Ignore(u => u.Channel);
            b.Ignore(u => u.Pronoun);
        });

        // UserIdentity is minted alongside the User by the platform-agnostic resolver the viewer resolver calls;
        // map it scalar-only (its User/Connection navs point at entities not hosted here — drop them).
        modelBuilder.Entity<UserIdentity>(b =>
        {
            b.HasKey(i => i.Id);
            b.Ignore(i => i.User);
            b.Ignore(i => i.Connection);
        });

        // ChannelEvent is the channel-event-log read model (F.4); map it minimally so TwitchChannelEventLogProjection
        // can fold into it. The production config types Data as jsonb (Npgsql-only) and the Channel/User navs point at
        // entities this context does not host — drop both so the SQLite provider maps a plain text column with no FK.
        modelBuilder.Entity<ChannelEvent>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.Data);
            b.Ignore(e => e.Channel);
            b.Ignore(e => e.User);
        });

        // EF discovers an entity type from every DbSet<T> property (an IApplicationDbContext requirement) and would
        // try to map the jsonb-of-complex-type columns those carry (e.g. ChatMessage's ChatEmote list), which the
        // SQLite provider cannot. Ignore every entity this proof does not host so the model stays minimal — exactly
        // as the sibling EventStoreTestDbContext does.
        modelBuilder.Ignore<Channel>();
        modelBuilder.Ignore<Pronoun>();
        modelBuilder.Ignore<ChannelModerator>();
        modelBuilder.Ignore<ChannelSubscription>();
        modelBuilder.Ignore<NomNomzBot.Domain.Platform.Entities.Service>();
        modelBuilder.Ignore<NomNomzBot.Domain.Commands.Entities.Command>();
        modelBuilder.Ignore<NomNomzBot.Domain.Rewards.Entities.Reward>();
        modelBuilder.Ignore<NomNomzBot.Domain.Quotes.Entities.Quote>();
        modelBuilder.Ignore<NomNomzBot.Domain.PickLists.Entities.PickList>();
        modelBuilder.Ignore<NomNomzBot.Domain.Widgets.Entities.Widget>();
        modelBuilder.Ignore<NomNomzBot.Domain.Widgets.Entities.WidgetVersion>();
        modelBuilder.Ignore<NomNomzBot.Domain.Widgets.Entities.WidgetGalleryItem>();
        modelBuilder.Ignore<NomNomzBot.Domain.Widgets.Entities.WidgetGallerySubmissionEvent>();
        modelBuilder.Ignore<NomNomzBot.Domain.Platform.Entities.EventSubSubscription>();
        modelBuilder.Ignore<NomNomzBot.Domain.Platform.Entities.EventSubConduit>();
        modelBuilder.Ignore<NomNomzBot.Domain.Platform.Entities.EventSubConduitShard>();
        modelBuilder.Ignore<NomNomzBot.Domain.Platform.Entities.IdempotencyKey>();
        modelBuilder.Ignore<NomNomzBot.Domain.Chat.Entities.ChatMessage>();
        modelBuilder.Ignore<NomNomzBot.Domain.Stream.Entities.Stream>();
        modelBuilder.Ignore<NomNomzBot.Domain.Platform.Entities.Configuration>();
        modelBuilder.Ignore<NomNomzBot.Domain.Platform.Entities.Storage>();
        modelBuilder.Ignore<NomNomzBot.Domain.Platform.Entities.Record>();
        modelBuilder.Ignore<Permission>();
        modelBuilder.Ignore<NomNomzBot.Domain.Platform.Entities.ChannelFeature>();
        modelBuilder.Ignore<ChannelBotAuthorization>();
        modelBuilder.Ignore<BotAccount>();
        modelBuilder.Ignore<AuthSession>();
        modelBuilder.Ignore<RefreshToken>();
        modelBuilder.Ignore<IpcDevModeKey>();
        modelBuilder.Ignore<NomNomzBot.Domain.Integrations.Entities.IntegrationConnection>();
        modelBuilder.Ignore<NomNomzBot.Domain.Integrations.Entities.IntegrationToken>();
        modelBuilder.Ignore<CryptoKey>();
        modelBuilder.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordGuildConnection>();
        modelBuilder.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordNotificationConfig>();
        modelBuilder.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordNotificationRole>();
        modelBuilder.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordMemberOptIn>();
        modelBuilder.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordNotificationDispatch>();
        modelBuilder.Ignore<NomNomzBot.Domain.Tts.Entities.TtsVoice>();
        modelBuilder.Ignore<NomNomzBot.Domain.Tts.Entities.UserTtsVoice>();
        modelBuilder.Ignore<NomNomzBot.Domain.Tts.Entities.TtsUsageRecord>();
        modelBuilder.Ignore<NomNomzBot.Domain.Tts.Entities.TtsCacheEntry>();
        modelBuilder.Ignore<NomNomzBot.Domain.Platform.Entities.DeletionAuditLog>();
        modelBuilder.Ignore<NomNomzBot.Domain.Commands.Entities.Timer>();
        modelBuilder.Ignore<NomNomzBot.Domain.Commands.Entities.EventResponse>();
        modelBuilder.Ignore<NomNomzBot.Domain.Rewards.Entities.WatchStreak>();
        modelBuilder.Ignore<NomNomzBot.Domain.Commands.Entities.Pipeline>();
        modelBuilder.Ignore<ChannelMembership>();
        modelBuilder.Ignore<ChannelCommunityStanding>();
        modelBuilder.Ignore<ActionDefinition>();
        modelBuilder.Ignore<ChannelActionOverride>();
        modelBuilder.Ignore<PermitGrant>();
        modelBuilder.Ignore<ChannelMissingScope>();
        modelBuilder.Ignore<IamPermission>();
        modelBuilder.Ignore<IamRole>();
        modelBuilder.Ignore<IamRolePermission>();
        modelBuilder.Ignore<IamPrincipal>();
        modelBuilder.Ignore<IamRoleAssignment>();
        modelBuilder.Ignore<IamAuditLog>();
        modelBuilder.Ignore<ConsentRecord>();
        modelBuilder.Ignore<NomNomzBot.Domain.Economy.Entities.CurrencyConfig>();
        modelBuilder.Ignore<NomNomzBot.Domain.Economy.Entities.EarningRule>();
        modelBuilder.Ignore<NomNomzBot.Domain.Economy.Entities.CurrencyAccount>();
        modelBuilder.Ignore<NomNomzBot.Domain.Economy.Entities.CurrencyLedgerEntry>();
        modelBuilder.Ignore<NomNomzBot.Domain.Economy.Entities.CatalogItem>();
        modelBuilder.Ignore<NomNomzBot.Domain.Economy.Entities.CatalogPurchase>();
        modelBuilder.Ignore<NomNomzBot.Domain.Economy.Entities.GameConfig>();
        modelBuilder.Ignore<NomNomzBot.Domain.Economy.Entities.GamePlay>();
        modelBuilder.Ignore<NomNomzBot.Domain.Economy.Entities.ViewerAgeConsent>();
        modelBuilder.Ignore<NomNomzBot.Domain.Economy.Entities.SavingsJar>();
        modelBuilder.Ignore<NomNomzBot.Domain.Economy.Entities.SavingsJarMembership>();
        modelBuilder.Ignore<NomNomzBot.Domain.Economy.Entities.JarContribution>();
        modelBuilder.Ignore<NomNomzBot.Domain.Economy.Entities.LeaderboardConfig>();
        modelBuilder.Ignore<NomNomzBot.Domain.Economy.Entities.LeaderboardOptOut>();
        modelBuilder.Ignore<NomNomzBot.Domain.Economy.Entities.LeaderboardSnapshot>();
        modelBuilder.Ignore<NomNomzBot.Domain.Billing.Entities.BillingTier>();
        modelBuilder.Ignore<NomNomzBot.Domain.Billing.Entities.TierLimit>();
        modelBuilder.Ignore<NomNomzBot.Domain.Billing.Entities.Subscription>();
        modelBuilder.Ignore<NomNomzBot.Domain.Billing.Entities.Invoice>();
        modelBuilder.Ignore<NomNomzBot.Domain.Billing.Entities.UsageRecord>();
        modelBuilder.Ignore<NomNomzBot.Domain.Billing.Entities.FoundersBadge>();
        modelBuilder.Ignore<NomNomzBot.Domain.Billing.Entities.InviteCode>();
        modelBuilder.Ignore<NomNomzBot.Domain.Federation.Entities.FederationPeer>();
        modelBuilder.Ignore<NomNomzBot.Domain.Federation.Entities.FederationPeerKey>();
        modelBuilder.Ignore<NomNomzBot.Domain.Federation.Entities.ChannelFederationOptIn>();
        modelBuilder.Ignore<NomNomzBot.Domain.Webhooks.Entities.OutboundWebhookEndpoint>();
        modelBuilder.Ignore<NomNomzBot.Domain.Webhooks.Entities.OutboundWebhookDelivery>();
        modelBuilder.Ignore<NomNomzBot.Domain.Webhooks.Entities.InboundWebhookEndpoint>();
        modelBuilder.Ignore<NomNomzBot.Domain.Platform.Entities.HttpEgressAllowlist>();
        modelBuilder.Ignore<NomNomzBot.Domain.Platform.Entities.FeatureFlag>();
        modelBuilder.Ignore<NomNomzBot.Domain.Platform.Entities.FeatureFlagOverride>();
        modelBuilder.Ignore<NomNomzBot.Domain.CustomCode.Entities.CodeScript>();
        modelBuilder.Ignore<NomNomzBot.Domain.CustomCode.Entities.CodeScriptVersion>();
    }

    // ── Everything else on IApplicationDbContext is unused by this proof ──
    public DbSet<ConsentRecord> ConsentRecords => throw new NotSupportedException();
    public DbSet<Channel> Channels => throw new NotSupportedException();
    public DbSet<ChannelModerator> ChannelModerators => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.Service> Services =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.Command> Commands =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.Reward> Rewards =>
        throw new NotSupportedException();
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
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Chat.Entities.YouTubeLiveChatBan> YouTubeLiveChatBans =>
        throw new NotSupportedException();
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
    public DbSet<NomNomzBot.Domain.Stream.Entities.Stream> Streams =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.Configuration> Configurations =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.Storage> Storages =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.Record> Records =>
        throw new NotSupportedException();
    public DbSet<Permission> Permissions => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.ChannelFeature> ChannelFeatures =>
        throw new NotSupportedException();
    public DbSet<ChannelBotAuthorization> ChannelBotAuthorizations =>
        throw new NotSupportedException();
    public DbSet<BotAccount> BotAccounts => throw new NotSupportedException();
    public DbSet<AuthSession> AuthSessions => throw new NotSupportedException();
    public DbSet<RefreshToken> RefreshTokens => throw new NotSupportedException();
    public DbSet<IpcDevModeKey> IpcDevModeKeys => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Integrations.Entities.IntegrationConnection> IntegrationConnections =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Integrations.Entities.IntegrationToken> IntegrationTokens =>
        throw new NotSupportedException();
    public DbSet<CryptoKey> CryptoKeys => throw new NotSupportedException();
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
    public DbSet<ChannelSubscription> ChannelSubscriptions => throw new NotSupportedException();
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
    public DbSet<Pronoun> Pronouns => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.DeletionAuditLog> DeletionAuditLogs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.Timer> Timers =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.EventResponse> EventResponses =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.WatchStreak> WatchStreaks =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.Pipeline> Pipelines =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.PipelineStep> PipelineSteps =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.PipelineStepCondition> PipelineStepConditions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.PipelineExecution> PipelineExecutions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.ChannelBuiltinCommand> ChannelBuiltinCommands =>
        throw new NotSupportedException();
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
        throw new NotSupportedException();
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
    public DbSet<IamAuditLog> IamAuditLogs => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.CurrencyConfig> CurrencyConfigs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.EarningRule> EarningRules =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.CurrencyAccount> CurrencyAccounts =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.CurrencyLedgerEntry> CurrencyLedgerEntries =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.CatalogItem> CatalogItems =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.CatalogPurchase> CatalogPurchases =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.GameConfig> GameConfigs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.GamePlay> GamePlays =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.ViewerAgeConsent> ViewerAgeConsents =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.SavingsJar> SavingsJars =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.SavingsJarMembership> SavingsJarMemberships =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.JarContribution> JarContributions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.LeaderboardConfig> LeaderboardConfigs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.LeaderboardOptOut> LeaderboardOptOuts =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.LeaderboardSnapshot> LeaderboardSnapshots =>
        throw new NotSupportedException();
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
    public DbSet<NomNomzBot.Domain.Webhooks.Entities.OutboundWebhookEndpoint> OutboundWebhookEndpoints =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Webhooks.Entities.OutboundWebhookDelivery> OutboundWebhookDeliveries =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Webhooks.Entities.InboundWebhookEndpoint> InboundWebhookEndpoints =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.HttpEgressAllowlist> HttpEgressAllowlists =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.FeatureFlag> FeatureFlags =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.FeatureFlagOverride> FeatureFlagOverrides =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.CustomCode.Entities.CodeScript> CodeScripts =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.CustomCode.Entities.CodeScriptVersion> CodeScriptVersions =>
        throw new NotSupportedException();
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

/// <summary>Opens a fresh, isolated SQLite database (one connection kept open for the test's lifetime).</summary>
internal sealed class ReadModelRebuildDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    private ReadModelRebuildDatabase(SqliteConnection connection) => _connection = connection;

    public static ReadModelRebuildDatabase Open()
    {
        SqliteConnection connection = new("DataSource=:memory:");
        connection.Open();
        ReadModelRebuildDatabase db = new(connection);
        using ReadModelRebuildDbContext context = db.NewContext();
        context.Database.EnsureCreated();
        return db;
    }

    public ReadModelRebuildDbContext NewContext()
    {
        DbContextOptions<ReadModelRebuildDbContext> options =
            new DbContextOptionsBuilder<ReadModelRebuildDbContext>().UseSqlite(_connection).Options;
        return new ReadModelRebuildDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}
