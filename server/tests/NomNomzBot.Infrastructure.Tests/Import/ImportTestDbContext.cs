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
using NomNomzBot.Infrastructure.Platform.Persistence.Converters;
using NomNomzBot.Infrastructure.Platform.Persistence.Extensions;
using NomNomzBot.Infrastructure.Platform.Persistence.Interceptors;
using NomNomzBot.Infrastructure.Quotes.Persistence;
using DomainCommand = NomNomzBot.Domain.Commands.Entities.Command;
using DomainTimer = NomNomzBot.Domain.Commands.Entities.Timer;

namespace NomNomzBot.Infrastructure.Tests.Import;

/// <summary>
/// A focused <see cref="IApplicationDbContext"/> over the entities the StreamElements import touches through its
/// three backing services — <see cref="DomainCommand"/>, <see cref="DomainTimer"/>, <see cref="Quote"/>, the
/// per-tenant sequence rows (quote numbering), and the channels they validate against. It runs on a real
/// relational SQLite connection so the quote add-under-transaction path and the unique
/// <c>(BroadcasterId, Number)</c> / command-name dedup all execute for real. Every other entity throws — the
/// import never reaches it. Mirrors <c>QuoteTestDbContext</c> with Command + Timer added to the live slice.
/// </summary>
internal sealed class ImportTestDbContext : DbContext, IApplicationDbContext
{
    public ImportTestDbContext(DbContextOptions<ImportTestDbContext> options)
        : base(options) { }

    public DbSet<DomainCommand> Commands => Set<DomainCommand>();
    public DbSet<DomainTimer> Timers => Set<DomainTimer>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<NomNomzBot.Domain.Music.Entities.BlockedTrack> BlockedTracks =>
        throw new NotSupportedException();
    public DbSet<TenantSequence> TenantSequences => Set<TenantSequence>();
    public DbSet<Channel> Channels => Set<Channel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Channel FIRST as a minimal standalone key so the FK/existence checks bind without EF walking the real
        // chat/stream value-object graph SQLite cannot host.
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

        modelBuilder.Entity<DomainCommand>(e =>
        {
            e.HasKey(c => c.Id);
            e.Ignore(c => c.Pipeline);
            e.Ignore(c => c.Channel);
            e.Property(c => c.Aliases)
                .HasConversion(
                    JsonValueConverter.Converter<List<string>>(),
                    JsonValueConverter.Comparer<List<string>>()
                );
            e.Property(c => c.TemplateResponses)
                .HasConversion(
                    JsonValueConverter.Converter<List<string>>()!,
                    JsonValueConverter.Comparer<List<string>>()!
                );
        });

        modelBuilder.Entity<DomainTimer>(e =>
        {
            e.HasKey(t => t.Id);
            e.Ignore(t => t.Pipeline);
            e.Ignore(t => t.Channel);
            e.Property(t => t.Messages)
                .HasConversion(
                    JsonValueConverter.Converter<List<string>>(),
                    JsonValueConverter.Comparer<List<string>>()
                );
        });

        modelBuilder.ApplyConfiguration(new QuoteConfiguration());
        modelBuilder.ApplyConfiguration(
            new NomNomzBot.Infrastructure.EventStore.Persistence.TenantSequenceConfiguration()
        );

        // EF discovers an entity type from EVERY DbSet<T> member — even the throwing ones — and would then map
        // their jsonb-of-complex-type columns, unsupported on SQLite. Ignore every entity outside this slice.
        foreach (Type entity in UnmappedEntities)
            modelBuilder.Ignore(entity);

        modelBuilder.ApplySoftDeleteFilter<Quote>();
    }

    private static readonly HashSet<Type> Mapped =
    [
        typeof(DomainCommand),
        typeof(DomainTimer),
        typeof(Quote),
        typeof(TenantSequence),
        typeof(Channel),
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
    public DbSet<NomNomzBot.Domain.Identity.Entities.ErasureRequest> ErasureRequests =>
        throw new NotSupportedException();
    public DbSet<ChannelModerator> ChannelModerators => throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.Service> Services =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.Reward> Rewards =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.Redemption> Redemptions =>
        throw new NotSupportedException();
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
    public DbSet<NomNomzBot.Domain.Community.Entities.ChatPoll> ChatPolls =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Community.Entities.ChatPollVote> ChatPollVotes =>
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
    public DbSet<NomNomzBot.Domain.Identity.Entities.KeyUsageBinding> KeyUsageBindings =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.EventStore.Entities.EventSubjectKey> EventSubjectKeys =>
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
    public DbSet<NomNomzBot.Domain.Vts.Entities.VtsConnection> VtsConnections =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Obs.Entities.ObsConnection> ObsConnections =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Automation.Entities.AutomationApiToken> AutomationApiTokens =>
        throw new NotSupportedException();
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
    public DbSet<NomNomzBot.Domain.Platform.Entities.DeletionAuditLog> DeletionAuditLogs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ComplianceAuditLog> ComplianceAuditLogs =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.EventResponse> EventResponses =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Rewards.Entities.WatchStreak> WatchStreaks =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Commands.Entities.ScheduledPipelineTask> ScheduledPipelineTasks =>
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
    public DbSet<NomNomzBot.Domain.Marketplace.Entities.InstalledBundle> InstalledBundles =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Economy.Entities.GameSession> GameSessions =>
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
    public DbSet<NomNomzBot.Domain.Assets.Entities.ChannelAsset> ChannelAssets =>
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

/// <summary>The real <see cref="IUnitOfWork"/> contract over the SQLite test context (for the quote add-under-transaction path).</summary>
internal sealed class ImportTestUnitOfWork : IUnitOfWork
{
    private readonly ImportTestDbContext _db;
    private Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? _transaction;

    public ImportTestUnitOfWork(ImportTestDbContext db) => _db = db;

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

/// <summary>Opens a fresh, isolated in-memory SQLite database (one connection kept open for the test's lifetime).</summary>
internal sealed class ImportSqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    private ImportSqliteTestDatabase(SqliteConnection connection) => _connection = connection;

    public static ImportSqliteTestDatabase Open()
    {
        SqliteConnection connection = new("DataSource=:memory:");
        connection.Open();
        ImportSqliteTestDatabase db = new(connection);
        using ImportTestDbContext context = db.NewContext();
        context.Database.EnsureCreated();
        return db;
    }

    public ImportTestDbContext NewContext()
    {
        DbContextOptions<ImportTestDbContext> options =
            new DbContextOptionsBuilder<ImportTestDbContext>()
                .UseSqlite(_connection)
                .AddInterceptors(new SoftDeleteInterceptor(TimeProvider.System))
                .Options;
        return new ImportTestDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}
