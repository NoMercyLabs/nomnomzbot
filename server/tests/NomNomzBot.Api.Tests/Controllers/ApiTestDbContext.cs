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
using NomNomzBot.Domain.Quotes.Entities;
using NomNomzBot.Domain.Rewards.Entities;
using NomNomzBot.Domain.Sound.Entities;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// A focused <see cref="IApplicationDbContext"/> over only the few entities the Api controller/authorization
/// tests read — platform <see cref="Configuration"/> rows (the Discord client credentials),
/// <see cref="Channel"/>, <see cref="DiscordGuildConnection"/>, and the six Plane-C IAM tables (so the
/// platform-IAM handler tests run the REAL <c>PlatformIamService</c> against a seeded store) — on the EF Core
/// InMemory provider. Everything else throws, since these tests never reach it. The
/// <c>DiscordGuildConnection</c> soft-delete global filter is applied so the "non-deleted connection" read
/// semantics match production.
/// </summary>
internal sealed class ApiTestDbContext : DbContext, IApplicationDbContext
{
    private ApiTestDbContext(DbContextOptions<ApiTestDbContext> options)
        : base(options) { }

    public static ApiTestDbContext New() =>
        new(
            new DbContextOptionsBuilder<ApiTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    public DbSet<NomNomzBot.Domain.Platform.Entities.Configuration> Configurations =>
        Set<NomNomzBot.Domain.Platform.Entities.Configuration>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.Redemption> Redemptions =>
        Set<NomNomzBot.Domain.Rewards.Entities.Redemption>();
    public DbSet<DiscordGuildConnection> DiscordGuildConnections => Set<DiscordGuildConnection>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<NomNomzBot.Domain.Platform.Entities.Configuration>().HasKey(e => e.Id);
        b.Entity<NomNomzBot.Domain.Platform.Entities.Configuration>().Ignore(e => e.Channel);

        b.Entity<Channel>().HasKey(e => e.Id);
        b.Entity<Channel>()
            .Ignore(e => e.Tags)
            .Ignore(e => e.ContentLabels)
            .Ignore(e => e.User)
            .Ignore(e => e.Moderators)
            .Ignore(e => e.Streams)
            .Ignore(e => e.Events);

        b.Entity<DiscordGuildConnection>().HasKey(e => e.Id);
        b.Entity<DiscordGuildConnection>().Ignore(e => e.Channel);
        b.Entity<DiscordGuildConnection>().HasQueryFilter(e => e.DeletedAt == null);

        // Plane-C IAM tables — scalar/enum-only, so they materialize on InMemory as-is.
        b.Entity<IamPermission>().HasKey(e => e.Id);
        b.Entity<IamRole>().HasKey(e => e.Id);
        b.Entity<IamRolePermission>().HasKey(e => e.Id);
        b.Entity<IamPrincipal>().HasKey(e => e.Id);
        b.Entity<IamRoleAssignment>().HasKey(e => e.Id);
        b.Entity<IamAuditLog>().HasKey(e => e.Id);

        // EF discovers entity types from the DbSet<T> property declarations regardless of the throwing getter
        // bodies; ignore every entity these tests do not exercise so the model stays minimal + provider-agnostic.
        foreach (Type entity in UnmappedEntities)
            b.Ignore(entity);
    }

    private static readonly HashSet<Type> Mapped =
    [
        typeof(NomNomzBot.Domain.Platform.Entities.Configuration),
        typeof(Channel),
        typeof(DiscordGuildConnection),
        typeof(IamPermission),
        typeof(IamRole),
        typeof(IamRolePermission),
        typeof(IamPrincipal),
        typeof(IamRoleAssignment),
        typeof(IamAuditLog),
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
    public DbSet<User> Users => throw new NotSupportedException();
    public DbSet<UserIdentity> UserIdentities => throw new NotSupportedException();
    public DbSet<ConsentRecord> ConsentRecords => throw new NotSupportedException();
    public DbSet<ChannelModerator> ChannelModerators => throw new NotSupportedException();
    public DbSet<Service> Services => throw new NotSupportedException();
    public DbSet<Command> Commands => throw new NotSupportedException();
    public DbSet<Reward> Rewards => throw new NotSupportedException();
    public DbSet<Quote> Quotes => throw new NotSupportedException();
    public DbSet<Widget> Widgets => throw new NotSupportedException();
    public DbSet<EventSubSubscription> EventSubSubscriptions => throw new NotSupportedException();
    public DbSet<EventSubConduit> EventSubConduits => throw new NotSupportedException();
    public DbSet<EventSubConduitShard> EventSubConduitShards => throw new NotSupportedException();
    public DbSet<IdempotencyKey> IdempotencyKeys => throw new NotSupportedException();
    public DbSet<ChatMessage> ChatMessages => throw new NotSupportedException();
    public DbSet<ChannelEvent> ChannelEvents => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Stream.Entities.Stream> Streams =>
        throw new NotSupportedException();
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
    public DbSet<IntegrationConnection> IntegrationConnections => throw new NotSupportedException();
    public DbSet<IntegrationToken> IntegrationTokens => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.CryptoKey> CryptoKeys =>
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
    public DbSet<Pronoun> Pronouns => throw new NotSupportedException();
    public DbSet<DeletionAuditLog> DeletionAuditLogs => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.Timer> Timers =>
        throw new NotSupportedException();
    public DbSet<EventResponse> EventResponses => throw new NotSupportedException();
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
    public DbSet<IamPermission> IamPermissions => Set<IamPermission>();
    public DbSet<IamRole> IamRoles => Set<IamRole>();
    public DbSet<IamRolePermission> IamRolePermissions => Set<IamRolePermission>();
    public DbSet<IamPrincipal> IamPrincipals => Set<IamPrincipal>();
    public DbSet<IamRoleAssignment> IamRoleAssignments => Set<IamRoleAssignment>();
    public DbSet<IamAuditLog> IamAuditLogs => Set<IamAuditLog>();
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
    public DbSet<FeatureFlag> FeatureFlags => throw new NotSupportedException();
    public DbSet<FeatureFlagOverride> FeatureFlagOverrides => throw new NotSupportedException();
    public DbSet<CodeScript> CodeScripts => throw new NotSupportedException();
    public DbSet<CodeScriptVersion> CodeScriptVersions => throw new NotSupportedException();
    public DbSet<SoundClip> SoundClips => throw new NotSupportedException();
    public DbSet<CustomDataSource> CustomDataSources => throw new NotSupportedException();
}
