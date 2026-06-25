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
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<ChannelModerator> ChannelModerators => Set<ChannelModerator>();
    public DbSet<Service> Services => Set<Service>();

    // Bot features
    public DbSet<Command> Commands => Set<Command>();
    public DbSet<Reward> Rewards => Set<Reward>();
    public DbSet<Redemption> Redemptions => Set<Redemption>();
    public DbSet<NomNomzBot.Domain.Quotes.Entities.Quote> Quotes =>
        Set<NomNomzBot.Domain.Quotes.Entities.Quote>();
    public DbSet<Widget> Widgets => Set<Widget>();

    // EventSub — per-tenant subscription registry (F.7), app-global conduit + shards (F.8/F.9),
    // and the scoped idempotency markers the notification dispatcher dedupes on (O.4).
    public DbSet<EventSubSubscription> EventSubSubscriptions => Set<EventSubSubscription>();
    public DbSet<EventSubConduit> EventSubConduits => Set<EventSubConduit>();
    public DbSet<EventSubConduitShard> EventSubConduitShards => Set<EventSubConduitShard>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

    // Chat
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChannelEvent> ChannelEvents => Set<ChannelEvent>();
    public DbSet<global::NomNomzBot.Domain.Stream.Entities.Stream> Streams =>
        Set<global::NomNomzBot.Domain.Stream.Entities.Stream>();

    // Config & Storage
    public DbSet<NomNomzBot.Domain.Platform.Entities.Configuration> Configurations =>
        Set<NomNomzBot.Domain.Platform.Entities.Configuration>();
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
    public DbSet<NomNomzBot.Domain.Identity.Entities.CryptoKey> CryptoKeys =>
        Set<NomNomzBot.Domain.Identity.Entities.CryptoKey>();

    // TTS
    public DbSet<TtsVoice> TtsVoices => Set<TtsVoice>();
    public DbSet<UserTtsVoice> UserTtsVoices => Set<UserTtsVoice>();
    public DbSet<TtsUsageRecord> TtsUsageRecords => Set<TtsUsageRecord>();
    public DbSet<TtsCacheEntry> TtsCacheEntries => Set<TtsCacheEntry>();

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

    // Pipelines
    public DbSet<Domain.Commands.Entities.Pipeline> Pipelines =>
        Set<Domain.Commands.Entities.Pipeline>();

    // Event store — append-only journal (O.1), per-tenant sequences (Q.3), projection checkpoints (O.3)
    public DbSet<EventJournal> EventJournals => Set<EventJournal>();
    public DbSet<TenantSequence> TenantSequences => Set<TenantSequence>();
    public DbSet<ProjectionCheckpoint> ProjectionCheckpoints => Set<ProjectionCheckpoint>();

    // Roles & permissions (Plane A/B)
    public DbSet<ChannelMembership> ChannelMemberships => Set<ChannelMembership>();
    public DbSet<ChannelCommunityStanding> ChannelCommunityStandings =>
        Set<ChannelCommunityStanding>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ActionDefinition> ActionDefinitions =>
        Set<NomNomzBot.Domain.Identity.Entities.ActionDefinition>();
    public DbSet<ChannelActionOverride> ChannelActionOverrides => Set<ChannelActionOverride>();
    public DbSet<PermitGrant> PermitGrants => Set<PermitGrant>();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelMissingScope> ChannelMissingScopes =>
        Set<NomNomzBot.Domain.Identity.Entities.ChannelMissingScope>();

    // Platform IAM (Plane C)
    public DbSet<IamPermission> IamPermissions => Set<IamPermission>();
    public DbSet<IamRole> IamRoles => Set<IamRole>();
    public DbSet<IamRolePermission> IamRolePermissions => Set<IamRolePermission>();
    public DbSet<IamPrincipal> IamPrincipals => Set<IamPrincipal>();
    public DbSet<IamRoleAssignment> IamRoleAssignments => Set<IamRoleAssignment>();
    public DbSet<IamAuditLog> IamAuditLogs => Set<IamAuditLog>();

    // Economy — currency core (economy.md K.1-K.3)
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
    public DbSet<NomNomzBot.Domain.Platform.Entities.DeploymentProfile> DeploymentProfiles =>
        Set<NomNomzBot.Domain.Platform.Entities.DeploymentProfile>();
    public DbSet<NomNomzBot.Domain.Platform.Entities.FeatureFlag> FeatureFlags =>
        Set<NomNomzBot.Domain.Platform.Entities.FeatureFlag>();
    public DbSet<NomNomzBot.Domain.Platform.Entities.FeatureFlagOverride> FeatureFlagOverrides =>
        Set<NomNomzBot.Domain.Platform.Entities.FeatureFlagOverride>();
    public DbSet<NomNomzBot.Domain.CustomCode.Entities.CodeScript> CodeScripts =>
        Set<NomNomzBot.Domain.CustomCode.Entities.CodeScript>();
    public DbSet<NomNomzBot.Domain.CustomCode.Entities.CodeScriptVersion> CodeScriptVersions =>
        Set<NomNomzBot.Domain.CustomCode.Entities.CodeScriptVersion>();

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
