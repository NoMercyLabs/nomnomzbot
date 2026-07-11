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
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Quotes.Entities;
using NomNomzBot.Infrastructure.Platform.Persistence.Extensions;
using NomNomzBot.Infrastructure.Platform.Persistence.Interceptors;
using NomNomzBot.Infrastructure.Quotes.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Quotes;

/// <summary>
/// A focused <see cref="IApplicationDbContext"/> over ONLY the entities the quote service touches — quotes,
/// the per-tenant sequence rows, and the channels it validates against — applying the REAL <see cref="Quote"/>
/// configuration. Like <c>EventStoreTestDbContext</c>, it runs on a real relational SQLite connection so the
/// unique <c>(BroadcasterId, Number)</c> constraint (the "never reuse a number" guarantee) is actually
/// enforced and the add-under-transaction path exercises a true write transaction. The production
/// <c>AppDbContext</c> is Npgsql-bound (jsonb complex types) and cannot host a test provider, so only the
/// exercised slice is mapped; everything else throws.
/// </summary>
internal sealed class QuoteTestDbContext : DbContext, IApplicationDbContext
{
    public QuoteTestDbContext(DbContextOptions<QuoteTestDbContext> options)
        : base(options) { }

    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.Redemption> Redemptions =>
        Set<NomNomzBot.Domain.Rewards.Entities.Redemption>();
    public DbSet<TenantSequence> TenantSequences => Set<TenantSequence>();
    public DbSet<Channel> Channels => Set<Channel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Map Channel FIRST as a standalone key — minimal, only so the service's existence check works. It is
        // configured before Quote so the Quote→Channel FK below binds to this minimal mapping and EF never
        // walks Channel's real navigations (which would drag the chat/stream value-object graph that SQLite
        // cannot host). Its jsonb List<string> columns are ignored.
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

        modelBuilder.ApplyConfiguration(new QuoteConfiguration());
        modelBuilder.ApplyConfiguration(
            new NomNomzBot.Infrastructure.EventStore.Persistence.TenantSequenceConfiguration()
        );

        // EF discovers an entity type from EVERY DbSet<T> property on the context (an IApplicationDbContext
        // requirement) — even the throwing ones — and would then try to map their jsonb-of-complex-type
        // columns (e.g. ChatMessage's ChatEmote value objects), unsupported on SQLite. Ignore every entity
        // this slice does not exercise so the model stays minimal and provider-agnostic (mirrors
        // EventStoreTestDbContext).
        foreach (Type entity in UnmappedEntities)
            modelBuilder.Ignore(entity);

        // The production soft-delete global filter (schema §1.2) so a deleted quote disappears from reads while
        // its row + number survive — the behavior the "never reuse" and "random excludes deleted" tests prove.
        modelBuilder.ApplySoftDeleteFilter<Quote>();
    }

    /// <summary>
    /// Every <see cref="IApplicationDbContext"/> entity NOT in the quote slice. Derived by reflection from the
    /// interface's <c>DbSet&lt;T&gt;</c> members so it never silently drifts when the contract grows — only
    /// <see cref="Quote"/>, <see cref="TenantSequence"/>, and <see cref="Channel"/> are mapped.
    /// </summary>
    private static readonly IReadOnlyList<Type> UnmappedEntities = typeof(IApplicationDbContext)
        .GetProperties()
        .Where(p =>
            p.PropertyType.IsGenericType
            && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
        )
        .Select(p => p.PropertyType.GetGenericArguments()[0])
        .Where(t => t != typeof(Quote) && t != typeof(TenantSequence) && t != typeof(Channel))
        .ToList();

    // ── Unused IApplicationDbContext surface — never reached by these tests ──
    public DbSet<User> Users => throw new NotSupportedException();
    public DbSet<UserIdentity> UserIdentities => throw new NotSupportedException();
    public DbSet<ConsentRecord> ConsentRecords => throw new NotSupportedException();
    public DbSet<ChannelModerator> ChannelModerators => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.Service> Services =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.Command> Commands =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.Reward> Rewards =>
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
    public DbSet<NomNomzBot.Domain.Integrations.Entities.IntegrationConnection> IntegrationConnections =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Integrations.Entities.IntegrationToken> IntegrationTokens =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.CryptoKey> CryptoKeys =>
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
    public DbSet<ChannelSubscription> ChannelSubscriptions => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsVoice> TtsVoices =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.UserTtsVoice> UserTtsVoices =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsUsageRecord> TtsUsageRecords =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Tts.Entities.TtsCacheEntry> TtsCacheEntries =>
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
    public DbSet<NomNomzBot.Domain.Commands.Entities.CommandUsage> CommandUsages =>
        throw new NotSupportedException();
    public DbSet<EventJournal> EventJournals => throw new NotSupportedException();
    public DbSet<ProjectionCheckpoint> ProjectionCheckpoints => throw new NotSupportedException();
    public DbSet<ChannelMembership> ChannelMemberships => throw new NotSupportedException();
    public DbSet<ChannelCommunityStanding> ChannelCommunityStandings =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ActionDefinition> ActionDefinitions =>
        throw new NotSupportedException();
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
    public DbSet<NomNomzBot.Domain.Analytics.Entities.ChannelChatterDay> ChannelChatterDays =>
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
}

/// <summary>
/// The real <see cref="IUnitOfWork"/> contract over the SQLite test context. A SQLite write transaction is the
/// stand-in for the production per-tenant row lock — <c>BEGIN IMMEDIATE</c> excludes concurrent writers, the
/// same ambient-transaction contract the allocator and <see cref="NomNomzBot.Infrastructure.Quotes.QuoteService"/>
/// rely on.
/// </summary>
internal sealed class QuoteTestUnitOfWork : IUnitOfWork
{
    private readonly QuoteTestDbContext _db;
    private Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? _transaction;

    public QuoteTestUnitOfWork(QuoteTestDbContext db) => _db = db;

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
internal sealed class QuoteSqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    private QuoteSqliteTestDatabase(SqliteConnection connection) => _connection = connection;

    public static QuoteSqliteTestDatabase Open()
    {
        SqliteConnection connection = new("DataSource=:memory:");
        connection.Open();
        QuoteSqliteTestDatabase db = new(connection);
        using QuoteTestDbContext context = db.NewContext();
        context.Database.EnsureCreated();
        return db;
    }

    public QuoteTestDbContext NewContext()
    {
        // Register the production SoftDeleteInterceptor so Remove() becomes a soft delete (DeletedAt stamp),
        // not a physical row delete — the test then proves the number is retained and never reused.
        DbContextOptions<QuoteTestDbContext> options =
            new DbContextOptionsBuilder<QuoteTestDbContext>()
                .UseSqlite(_connection)
                .AddInterceptors(new SoftDeleteInterceptor(TimeProvider.System))
                .Options;
        return new QuoteTestDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}
