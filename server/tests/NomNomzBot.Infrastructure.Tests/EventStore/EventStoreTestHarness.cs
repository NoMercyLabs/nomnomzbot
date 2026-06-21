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

namespace NomNomzBot.Infrastructure.Tests.EventStore;

/// <summary>
/// A focused <see cref="IApplicationDbContext"/> over ONLY the event-store entities (journal, per-tenant
/// sequences, projection checkpoints), applying their REAL EF configurations. The production
/// <c>AppDbContext</c> is Npgsql-bound for complex-typed jsonb columns and cannot be hosted on a test provider,
/// so — exactly like <c>SeedTestDbContext</c>/<c>AuthDbContext</c> — this maps only what these tests exercise.
/// It runs on a real relational SQLite connection so the unique <c>(BroadcasterId, StreamPosition)</c> and
/// <c>(BroadcasterId, SequenceName)</c> constraints (the load-bearing concurrency guarantees) are actually
/// enforced. Every <see cref="IApplicationDbContext"/> member the event-store services never touch throws.
/// </summary>
internal sealed class EventStoreTestDbContext : DbContext, IApplicationDbContext
{
    public EventStoreTestDbContext(DbContextOptions<EventStoreTestDbContext> options)
        : base(options) { }

    public DbSet<EventJournal> EventJournals => Set<EventJournal>();
    public DbSet<TenantSequence> TenantSequences => Set<TenantSequence>();
    public DbSet<ProjectionCheckpoint> ProjectionCheckpoints => Set<ProjectionCheckpoint>();
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(
            new NomNomzBot.Infrastructure.EventStore.Persistence.EventJournalConfiguration()
        );
        modelBuilder.ApplyConfiguration(
            new NomNomzBot.Infrastructure.EventStore.Persistence.TenantSequenceConfiguration()
        );
        modelBuilder.ApplyConfiguration(
            new NomNomzBot.Infrastructure.EventStore.Persistence.ProjectionCheckpointConfiguration()
        );

        // EF discovers entity types from every DbSet<T> property (an IApplicationDbContext requirement) and
        // would try to map their jsonb-of-complex-type columns (unsupported on SQLite). Ignore every entity
        // these tests do not exercise so the model stays minimal and provider-agnostic.
        modelBuilder.Ignore<NomNomzBot.Domain.Identity.Entities.User>();
        modelBuilder.Ignore<NomNomzBot.Domain.Identity.Entities.Channel>();
        modelBuilder.Ignore<NomNomzBot.Domain.Identity.Entities.ChannelModerator>();
        modelBuilder.Ignore<NomNomzBot.Domain.Platform.Entities.Service>();
        modelBuilder.Ignore<NomNomzBot.Domain.Commands.Entities.Command>();
        modelBuilder.Ignore<NomNomzBot.Domain.Rewards.Entities.Reward>();
        modelBuilder.Ignore<NomNomzBot.Domain.Widgets.Entities.Widget>();
        modelBuilder.Ignore<NomNomzBot.Domain.Platform.Entities.EventSubSubscription>();
        modelBuilder.Ignore<NomNomzBot.Domain.Platform.Entities.EventSubConduit>();
        modelBuilder.Ignore<NomNomzBot.Domain.Platform.Entities.EventSubConduitShard>();
        modelBuilder.Ignore<NomNomzBot.Domain.Platform.Entities.IdempotencyKey>();
        modelBuilder.Ignore<NomNomzBot.Domain.Chat.Entities.ChatMessage>();
        modelBuilder.Ignore<NomNomzBot.Domain.Identity.Entities.ChannelEvent>();
        modelBuilder.Ignore<NomNomzBot.Domain.Stream.Entities.Stream>();
        modelBuilder.Ignore<NomNomzBot.Domain.Platform.Entities.Configuration>();
        modelBuilder.Ignore<NomNomzBot.Domain.Platform.Entities.Storage>();
        modelBuilder.Ignore<NomNomzBot.Domain.Platform.Entities.Record>();
        modelBuilder.Ignore<NomNomzBot.Domain.Identity.Entities.Permission>();
        modelBuilder.Ignore<NomNomzBot.Domain.Platform.Entities.ChannelFeature>();
        modelBuilder.Ignore<NomNomzBot.Domain.Identity.Entities.ChannelBotAuthorization>();
        modelBuilder.Ignore<NomNomzBot.Domain.Identity.Entities.BotAccount>();
        modelBuilder.Ignore<NomNomzBot.Domain.Identity.Entities.AuthSession>();
        modelBuilder.Ignore<NomNomzBot.Domain.Identity.Entities.RefreshToken>();
        modelBuilder.Ignore<NomNomzBot.Domain.Identity.Entities.IpcDevModeKey>();
        modelBuilder.Ignore<NomNomzBot.Domain.Integrations.Entities.IntegrationConnection>();
        modelBuilder.Ignore<NomNomzBot.Domain.Integrations.Entities.IntegrationToken>();
        modelBuilder.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordServerAuthorization>();
        modelBuilder.Ignore<NomNomzBot.Domain.Identity.Entities.ChannelSubscription>();
        modelBuilder.Ignore<NomNomzBot.Domain.Tts.Entities.TtsVoice>();
        modelBuilder.Ignore<NomNomzBot.Domain.Tts.Entities.UserTtsVoice>();
        modelBuilder.Ignore<NomNomzBot.Domain.Tts.Entities.TtsUsageRecord>();
        modelBuilder.Ignore<NomNomzBot.Domain.Tts.Entities.TtsCacheEntry>();
        modelBuilder.Ignore<NomNomzBot.Domain.Identity.Entities.Pronoun>();
        modelBuilder.Ignore<NomNomzBot.Domain.Platform.Entities.DeletionAuditLog>();
        modelBuilder.Ignore<NomNomzBot.Domain.Commands.Entities.Timer>();
        modelBuilder.Ignore<NomNomzBot.Domain.Commands.Entities.EventResponse>();
        modelBuilder.Ignore<NomNomzBot.Domain.Rewards.Entities.WatchStreak>();
        modelBuilder.Ignore<NomNomzBot.Domain.Commands.Entities.Pipeline>();
    }

    // ── Unused IApplicationDbContext surface — never reached by these tests ──
    public DbSet<NomNomzBot.Domain.Identity.Entities.User> Users =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.Channel> Channels =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelModerator> ChannelModerators =>
        throw new NotSupportedException();
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
    public DbSet<NomNomzBot.Domain.Identity.Entities.Permission> Permissions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Platform.Entities.ChannelFeature> ChannelFeatures =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelBotAuthorization> ChannelBotAuthorizations =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.BotAccount> BotAccounts =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.AuthSession> AuthSessions =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.RefreshToken> RefreshTokens =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.IpcDevModeKey> IpcDevModeKeys =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Integrations.Entities.IntegrationConnection> IntegrationConnections =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Integrations.Entities.IntegrationToken> IntegrationTokens =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Discord.Entities.DiscordServerAuthorization> DiscordServerAuthorizations =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.Identity.Entities.ChannelSubscription> ChannelSubscriptions =>
        throw new NotSupportedException();
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
}

/// <summary>
/// Adapts <see cref="EventStoreTestDbContext"/> to the <see cref="IUnitOfWork"/> the journal service drives. A
/// SQLite write transaction is the test stand-in for the production per-tenant row lock — <c>BEGIN IMMEDIATE</c>
/// excludes concurrent writers, the same ambient-transaction contract the allocator relies on.
/// </summary>
internal sealed class EventStoreTestUnitOfWork : IUnitOfWork
{
    private readonly EventStoreTestDbContext _db;
    private Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? _transaction;

    public EventStoreTestUnitOfWork(EventStoreTestDbContext db) => _db = db;

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
internal sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    private SqliteTestDatabase(SqliteConnection connection) => _connection = connection;

    public static SqliteTestDatabase Open()
    {
        SqliteConnection connection = new("DataSource=:memory:");
        connection.Open();
        SqliteTestDatabase db = new(connection);
        using EventStoreTestDbContext context = db.NewContext();
        context.Database.EnsureCreated();
        return db;
    }

    public EventStoreTestDbContext NewContext()
    {
        DbContextOptions<EventStoreTestDbContext> options =
            new DbContextOptionsBuilder<EventStoreTestDbContext>().UseSqlite(_connection).Options;
        return new EventStoreTestDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}
