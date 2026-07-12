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
using NomNomzBot.Domain.Analytics.Entities;
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
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Sound.Entities;
using NomNomzBot.Domain.Supporters.Entities;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Widgets.Entities;
using DomainTimer = NomNomzBot.Domain.Commands.Entities.Timer;
using RecordEntity = NomNomzBot.Domain.Platform.Entities.Record;

namespace NomNomzBot.Infrastructure.Tests.Supporters;

/// <summary>
/// A focused <see cref="IApplicationDbContext"/> over just the entities the supporter subsystem tests exercise —
/// on the EF Core InMemory provider (the production <c>AppDbContext</c> is Npgsql-bound and cannot host a test
/// provider). Maps <see cref="SupporterConnection"/> + <see cref="SupporterEvent"/> plus the few referenced
/// tables: <see cref="Channel"/>, <see cref="User"/>, <see cref="EventResponse"/> (the bound trigger response),
/// and <see cref="ChannelEvent"/> (the activity-feed row). Every other set throws — no exercised path reaches it.
/// </summary>
internal sealed class SupporterTestDbContext : DbContext, IApplicationDbContext
{
    private SupporterTestDbContext(DbContextOptions<SupporterTestDbContext> options)
        : base(options) { }

    public static SupporterTestDbContext New() =>
        new(
            new DbContextOptionsBuilder<SupporterTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    public DbSet<SupporterConnection> SupporterConnections => Set<SupporterConnection>();
    public DbSet<SupporterEvent> SupporterEvents => Set<SupporterEvent>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<User> Users => Set<User>();
    public DbSet<EventResponse> EventResponses => Set<EventResponse>();
    public DbSet<ChannelEvent> ChannelEvents => Set<ChannelEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<SupporterConnection>(e =>
        {
            e.HasKey(c => c.Id);
            e.Ignore(c => c.Channel);
        });

        b.Entity<SupporterEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Ignore(x => x.Channel);
            e.Ignore(x => x.SupporterUser);
        });

        b.Entity<Channel>(e =>
        {
            e.HasKey(c => c.Id);
            e.Ignore(c => c.User);
            e.Ignore(c => c.Moderators);
            e.Ignore(c => c.Streams);
            e.Ignore(c => c.Events);
        });

        b.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.Ignore(u => u.Pronoun);
            e.Ignore(u => u.AltPronoun);
            e.Ignore(u => u.Channel);
        });

        b.Entity<EventResponse>(e =>
        {
            e.HasKey(r => r.Id);
            e.Ignore(r => r.Pipeline);
            e.Ignore(r => r.Channel);
            e.Ignore(r => r.MetadataJson);
        });

        b.Entity<ChannelEvent>(e =>
        {
            e.HasKey(c => c.Id);
            e.Ignore(c => c.Channel);
            e.Ignore(c => c.User);
        });

        // EF discovers entity types from the DbSet<T> property declarations regardless of the throwing getter
        // bodies; ignore every entity these tests do not exercise so the model stays minimal + provider-agnostic.
        foreach (Type entity in UnmappedEntities)
            b.Ignore(entity);
    }

    private static readonly HashSet<Type> Mapped =
    [
        typeof(SupporterConnection),
        typeof(SupporterEvent),
        typeof(Channel),
        typeof(User),
        typeof(EventResponse),
        typeof(ChannelEvent),
    ];

    private static readonly IReadOnlyList<Type> UnmappedEntities = typeof(IApplicationDbContext)
        .GetProperties()
        .Where(p =>
            p.PropertyType.IsGenericType
            && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
        )
        .Select(p => p.PropertyType.GetGenericArguments()[0])
        .Where(t => !Mapped.Contains(t))
        .ToList();

    // ── Unused IApplicationDbContext surface — never reached by these tests ──
    public DbSet<RecordEntity> Records => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Moderation.Entities.ViewerReport> ViewerReports =>
        throw new NotSupportedException();
    public DbSet<UserIdentity> UserIdentities => throw new NotSupportedException();
    public DbSet<ConsentRecord> ConsentRecords => throw new NotSupportedException();
    public DbSet<ChannelModerator> ChannelModerators => throw new NotSupportedException();
    public DbSet<Service> Services => throw new NotSupportedException();
    public DbSet<Reward> Rewards => throw new NotSupportedException();
    public DbSet<Redemption> Redemptions => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Quotes.Entities.Quote> Quotes =>
        throw new NotSupportedException();
    public DbSet<Widget> Widgets => throw new NotSupportedException();
    public DbSet<EventSubSubscription> EventSubSubscriptions => throw new NotSupportedException();
    public DbSet<EventSubConduit> EventSubConduits => throw new NotSupportedException();
    public DbSet<EventSubConduitShard> EventSubConduitShards => throw new NotSupportedException();
    public DbSet<IdempotencyKey> IdempotencyKeys => throw new NotSupportedException();
    public DbSet<ChatMessage> ChatMessages => throw new NotSupportedException();
    public DbSet<YouTubeLiveChatBan> YouTubeLiveChatBans => throw new NotSupportedException();
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
    public DbSet<Configuration> Configurations => throw new NotSupportedException();
    public DbSet<Storage> Storages => throw new NotSupportedException();
    public DbSet<Command> Commands => throw new NotSupportedException();
    public DbSet<DomainTimer> Timers => throw new NotSupportedException();
    public DbSet<Permission> Permissions => throw new NotSupportedException();
    public DbSet<ChannelFeature> ChannelFeatures => throw new NotSupportedException();
    public DbSet<ChannelBotAuthorization> ChannelBotAuthorizations =>
        throw new NotSupportedException();
    public DbSet<BotAccount> BotAccounts => throw new NotSupportedException();
    public DbSet<AuthSession> AuthSessions => throw new NotSupportedException();
    public DbSet<RefreshToken> RefreshTokens => throw new NotSupportedException();
    public DbSet<IpcDevModeKey> IpcDevModeKeys => throw new NotSupportedException();
    public DbSet<IntegrationConnection> IntegrationConnections => throw new NotSupportedException();
    public DbSet<IntegrationToken> IntegrationTokens => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.CryptoKey> CryptoKeys =>
        throw new NotSupportedException();
    public DbSet<DiscordGuildConnection> DiscordGuildConnections =>
        throw new NotSupportedException();
    public DbSet<DiscordNotificationConfig> DiscordNotificationConfigs =>
        throw new NotSupportedException();
    public DbSet<DiscordNotificationRole> DiscordNotificationRoles =>
        throw new NotSupportedException();
    public DbSet<DiscordMemberOptIn> DiscordMemberOptIns => throw new NotSupportedException();
    public DbSet<DiscordNotificationDispatch> DiscordNotificationDispatches =>
        throw new NotSupportedException();
    public DbSet<ChannelSubscription> ChannelSubscriptions => throw new NotSupportedException();
    public DbSet<TtsVoice> TtsVoices => throw new NotSupportedException();
    public DbSet<UserTtsVoice> UserTtsVoices => throw new NotSupportedException();
    public DbSet<TtsUsageRecord> TtsUsageRecords => throw new NotSupportedException();
    public DbSet<TtsCacheEntry> TtsCacheEntries => throw new NotSupportedException();
    public DbSet<TtsApprovalQueueEntry> TtsApprovalQueueEntries =>
        throw new NotSupportedException();
    public DbSet<Pronoun> Pronouns => throw new NotSupportedException();
    public DbSet<DeletionAuditLog> DeletionAuditLogs => throw new NotSupportedException();
    public DbSet<WatchStreak> WatchStreaks => throw new NotSupportedException();
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
    public DbSet<EventJournal> EventJournals => throw new NotSupportedException();
    public DbSet<TenantSequence> TenantSequences => throw new NotSupportedException();
    public DbSet<ProjectionCheckpoint> ProjectionCheckpoints => throw new NotSupportedException();
    public DbSet<ChannelMembership> ChannelMemberships => throw new NotSupportedException();
    public DbSet<ChannelCommunityStanding> ChannelCommunityStandings =>
        throw new NotSupportedException();
    public DbSet<ActionDefinition> ActionDefinitions => throw new NotSupportedException();
    public DbSet<ChannelActionOverride> ChannelActionOverrides => throw new NotSupportedException();
    public DbSet<PermitGrant> PermitGrants => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelMissingScope> ChannelMissingScopes =>
        throw new NotSupportedException();
    public DbSet<IamPermission> IamPermissions => throw new NotSupportedException();
    public DbSet<IamRole> IamRoles => throw new NotSupportedException();
    public DbSet<IamRolePermission> IamRolePermissions => throw new NotSupportedException();
    public DbSet<IamPrincipal> IamPrincipals => throw new NotSupportedException();
    public DbSet<IamRoleAssignment> IamRoleAssignments => throw new NotSupportedException();
    public DbSet<IamAuditLog> IamAuditLogs => throw new NotSupportedException();
    public DbSet<CurrencyConfig> CurrencyConfigs => throw new NotSupportedException();
    public DbSet<EarningRule> EarningRules => throw new NotSupportedException();
    public DbSet<CurrencyAccount> CurrencyAccounts => throw new NotSupportedException();
    public DbSet<CurrencyLedgerEntry> CurrencyLedgerEntries => throw new NotSupportedException();
    public DbSet<CatalogItem> CatalogItems => throw new NotSupportedException();
    public DbSet<CatalogPurchase> CatalogPurchases => throw new NotSupportedException();
    public DbSet<GameConfig> GameConfigs => throw new NotSupportedException();
    public DbSet<GamePlay> GamePlays => throw new NotSupportedException();
    public DbSet<ViewerAgeConsent> ViewerAgeConsents => throw new NotSupportedException();
    public DbSet<SavingsJar> SavingsJars => throw new NotSupportedException();
    public DbSet<SavingsJarMembership> SavingsJarMemberships => throw new NotSupportedException();
    public DbSet<JarContribution> JarContributions => throw new NotSupportedException();
    public DbSet<LeaderboardConfig> LeaderboardConfigs => throw new NotSupportedException();
    public DbSet<LeaderboardOptOut> LeaderboardOptOuts => throw new NotSupportedException();
    public DbSet<LeaderboardSnapshot> LeaderboardSnapshots => throw new NotSupportedException();
    public DbSet<BillingTier> BillingTiers => throw new NotSupportedException();
    public DbSet<TierLimit> TierLimits => throw new NotSupportedException();
    public DbSet<Subscription> Subscriptions => throw new NotSupportedException();
    public DbSet<Invoice> Invoices => throw new NotSupportedException();
    public DbSet<UsageRecord> UsageRecords => throw new NotSupportedException();
    public DbSet<FoundersBadge> FoundersBadges => throw new NotSupportedException();
    public DbSet<InviteCode> InviteCodes => throw new NotSupportedException();
    public DbSet<FederationPeer> FederationPeers => throw new NotSupportedException();
    public DbSet<FederationPeerKey> FederationPeerKeys => throw new NotSupportedException();
    public DbSet<ChannelFederationOptIn> ChannelFederationOptIns =>
        throw new NotSupportedException();
    public DbSet<OutboundWebhookEndpoint> OutboundWebhookEndpoints =>
        throw new NotSupportedException();
    public DbSet<OutboundWebhookDelivery> OutboundWebhookDeliveries =>
        throw new NotSupportedException();
    public DbSet<InboundWebhookEndpoint> InboundWebhookEndpoints =>
        throw new NotSupportedException();
    public DbSet<HttpEgressAllowlist> HttpEgressAllowlists => throw new NotSupportedException();
    public DbSet<ViewerProfile> ViewerProfiles => throw new NotSupportedException();
    public DbSet<WatchSession> WatchSessions => throw new NotSupportedException();
    public DbSet<MessageActivityDaily> MessageActivityDailies => throw new NotSupportedException();
    public DbSet<ViewerEngagementDaily> ViewerEngagementDailies =>
        throw new NotSupportedException();
    public DbSet<ChannelAnalyticsDaily> ChannelAnalyticsDailies =>
        throw new NotSupportedException();
    public DbSet<ChannelChatterDay> ChannelChatterDays => throw new NotSupportedException();
    public DbSet<FeatureFlag> FeatureFlags => throw new NotSupportedException();
    public DbSet<FeatureFlagOverride> FeatureFlagOverrides => throw new NotSupportedException();
    public DbSet<CodeScript> CodeScripts => throw new NotSupportedException();
    public DbSet<CodeScriptVersion> CodeScriptVersions => throw new NotSupportedException();
    public DbSet<SoundClip> SoundClips => throw new NotSupportedException();
    public DbSet<CustomDataSource> CustomDataSources => throw new NotSupportedException();
}
