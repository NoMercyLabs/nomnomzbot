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
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Infrastructure.Discord.Persistence;
using NomNomzBot.Infrastructure.Platform.Persistence.Extensions;
using NomNomzBot.Infrastructure.Platform.Persistence.Interceptors;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// A focused <see cref="IApplicationDbContext"/> over ONLY the entities the Discord subsystem touches — the five
/// P.10 tables, the integration vault tables the token custody rides on, and the channels it validates against —
/// applying the REAL Discord <see cref="IEntityTypeConfiguration{T}"/>s. Runs on a real relational SQLite
/// connection so the unique <c>(NotificationConfigId, DedupeKey)</c> dedupe constraint and the
/// <c>(GuildConnectionId, TriggerType)</c> uniqueness are genuinely enforced and the <c>[VC:JSON]</c>
/// <c>EmbedConfig</c> Newtonsoft round-trip is exercised end to end. The production <c>AppDbContext</c> is
/// Npgsql-bound and cannot host a test provider, so only the exercised slice is mapped; everything else throws.
/// </summary>
internal sealed class DiscordTestDbContext : DbContext, IApplicationDbContext
{
    public DiscordTestDbContext(DbContextOptions<DiscordTestDbContext> options)
        : base(options) { }

    public DbSet<DiscordGuildConnection> DiscordGuildConnections => Set<DiscordGuildConnection>();
    public DbSet<DiscordNotificationConfig> DiscordNotificationConfigs =>
        Set<DiscordNotificationConfig>();
    public DbSet<DiscordNotificationRole> DiscordNotificationRoles =>
        Set<DiscordNotificationRole>();
    public DbSet<DiscordMemberOptIn> DiscordMemberOptIns => Set<DiscordMemberOptIn>();
    public DbSet<DiscordNotificationDispatch> DiscordNotificationDispatches =>
        Set<DiscordNotificationDispatch>();
    public DbSet<IntegrationConnection> IntegrationConnections => Set<IntegrationConnection>();
    public DbSet<IntegrationToken> IntegrationTokens => Set<IntegrationToken>();
    public DbSet<Channel> Channels => Set<Channel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Map Channel FIRST as a minimal standalone key so the Discord→Channel FKs bind to it and EF never walks
        // Channel's real navigations (which drag the chat/stream value-object graph SQLite cannot host).
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

        // Minimal integration-vault mapping (the [VC:JSON] Scopes list is ignored — not exercised here).
        modelBuilder.Entity<IntegrationConnection>(b =>
        {
            b.HasKey(c => c.Id);
            b.Ignore(c => c.Scopes);
            b.Ignore(c => c.Channel);
            b.Ignore(c => c.Tokens);
        });
        modelBuilder.Entity<IntegrationToken>(b => b.HasKey(t => t.Id));

        modelBuilder.ApplyConfiguration(new DiscordGuildConnectionConfiguration());
        modelBuilder.ApplyConfiguration(new DiscordNotificationConfigConfiguration());
        modelBuilder.ApplyConfiguration(new DiscordNotificationRoleConfiguration());
        modelBuilder.ApplyConfiguration(new DiscordMemberOptInConfiguration());
        modelBuilder.ApplyConfiguration(new DiscordNotificationDispatchConfiguration());

        foreach (Type entity in UnmappedEntities)
            modelBuilder.Ignore(entity);

        // The production soft-delete global filter so a disconnected/deleted row disappears from reads.
        modelBuilder.ApplySoftDeleteFilter<DiscordGuildConnection>();
        modelBuilder.ApplySoftDeleteFilter<DiscordNotificationConfig>();
        modelBuilder.ApplySoftDeleteFilter<DiscordNotificationRole>();
        modelBuilder.ApplySoftDeleteFilter<DiscordMemberOptIn>();
    }

    private static readonly HashSet<Type> Mapped =
    [
        typeof(DiscordGuildConnection),
        typeof(DiscordNotificationConfig),
        typeof(DiscordNotificationRole),
        typeof(DiscordMemberOptIn),
        typeof(DiscordNotificationDispatch),
        typeof(IntegrationConnection),
        typeof(IntegrationToken),
        typeof(Channel),
    ];

    /// <summary>Every <see cref="IApplicationDbContext"/> entity NOT in the Discord slice — derived by reflection.</summary>
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
    public DbSet<ConsentRecord> ConsentRecords => throw new NotSupportedException();
    public DbSet<ChannelModerator> ChannelModerators => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.Service> Services =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.Command> Commands =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.Reward> Rewards =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Quotes.Entities.Quote> Quotes =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Widgets.Entities.Widget> Widgets =>
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
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelEvent> ChannelEvents =>
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
    public DbSet<ChannelSubscription> ChannelSubscriptions => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsVoice> TtsVoices =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.UserTtsVoice> UserTtsVoices =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsUsageRecord> TtsUsageRecords =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsCacheEntry> TtsCacheEntries =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.Pronoun> Pronouns =>
        throw new NotSupportedException();
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
    public DbSet<NomNomzBot.Domain.EventStore.Entities.EventJournal> EventJournals =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.EventStore.Entities.TenantSequence> TenantSequences =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.EventStore.Entities.ProjectionCheckpoint> ProjectionCheckpoints =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelMembership> ChannelMemberships =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelCommunityStanding> ChannelCommunityStandings =>
        throw new NotSupportedException();
    public DbSet<ActionDefinition> ActionDefinitions => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelActionOverride> ChannelActionOverrides =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.PermitGrant> PermitGrants =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelMissingScope> ChannelMissingScopes =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IamPermission> IamPermissions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IamRole> IamRoles =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IamRolePermission> IamRolePermissions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IamPrincipal> IamPrincipals =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IamRoleAssignment> IamRoleAssignments =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IamAuditLog> IamAuditLogs =>
        throw new NotSupportedException();
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
    public DbSet<NomNomzBot.Domain.Platform.Entities.FeatureFlag> FeatureFlags =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.FeatureFlagOverride> FeatureFlagOverrides =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.CustomCode.Entities.CodeScript> CodeScripts =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.CustomCode.Entities.CodeScriptVersion> CodeScriptVersions =>
        throw new NotSupportedException();
}

/// <summary>The real <see cref="IUnitOfWork"/> contract over the SQLite test context (a write transaction).</summary>
internal sealed class DiscordTestUnitOfWork : IUnitOfWork
{
    private readonly DiscordTestDbContext _db;
    private Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? _transaction;

    public DiscordTestUnitOfWork(DiscordTestDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

    public async Task BeginTransactionAsync(CancellationToken ct = default) =>
        _transaction = await _db.Database.BeginTransactionAsync(ct);

    public async Task CommitTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction is not null)
        {
            await _transaction.CommitAsync(ct);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction is not null)
        {
            await _transaction.RollbackAsync(ct);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }
}

/// <summary>Opens a fresh, isolated SQLite database (one connection kept open for the test's lifetime).</summary>
internal sealed class DiscordSqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    private DiscordSqliteTestDatabase(SqliteConnection connection) => _connection = connection;

    public static DiscordSqliteTestDatabase Open()
    {
        SqliteConnection connection = new("DataSource=:memory:");
        connection.Open();
        DiscordSqliteTestDatabase db = new(connection);
        using DiscordTestDbContext context = db.NewContext();
        context.Database.EnsureCreated();
        return db;
    }

    public DiscordTestDbContext NewContext()
    {
        DbContextOptions<DiscordTestDbContext> options =
            new DbContextOptionsBuilder<DiscordTestDbContext>()
                .UseSqlite(_connection)
                .AddInterceptors(new SoftDeleteInterceptor(TimeProvider.System))
                .Options;
        return new DiscordTestDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}
