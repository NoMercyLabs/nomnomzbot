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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Auth;
using NomNomzBot.Infrastructure.Platform.Security;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Shared scaffolding for the auth behavior tests: a focused EF context mapping only the auth/integration
/// entities (so it runs on the InMemory provider, where the production <c>AppDbContext</c>'s
/// jsonb-of-complex-type columns cannot materialize), the REAL envelope-encryption crypto stack
/// (so the vault round-trip proves ciphertext-at-rest, not a stub), and a recording event bus.
/// </summary>
internal static class AuthTestBuilder
{
    // A fixed 32-byte base64 deployment key drives the deterministic KEK fallback (no OS keystore needed).
    private const string ConfigKey = "Zm9yLXRlc3Qtb25seS1rZWstMzItYnl0ZXMtbG9uZyEh";

    public static ITokenProtector RealTokenProtector(out ISubjectKeyService subjectKeys)
    {
        IFieldCipher cipher = new AesGcmFieldCipher();
        IKeyVault vault = new OsSecureStoreKeyVault(
            Options.Create(new EncryptionOptions { Key = ConfigKey }),
            NullLogger<OsSecureStoreKeyVault>.Instance
        );
        ISubjectKeyStore store = new InMemorySubjectKeyStore();
        subjectKeys = new SubjectKeyService(
            vault,
            cipher,
            store,
            TimeProvider.System,
            NullLogger<SubjectKeyService>.Instance
        );
        return new TokenProtector(subjectKeys, NullLogger<TokenProtector>.Instance);
    }

    public static AuthDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<AuthDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
}

/// <summary>Records every published domain event so a test can assert the side effect actually fired.</summary>
internal sealed class RecordingEventBus : IEventBus
{
    public List<IDomainEvent> Published { get; } = [];

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class, IDomainEvent
    {
        Published.Add(@event);
        return Task.CompletedTask;
    }

    public void PublishFireAndForget<TEvent>(TEvent @event)
        where TEvent : class, IDomainEvent => Published.Add(@event);
}

/// <summary>
/// Focused EF context over the auth/integration entities. Maps only what the services under test touch;
/// every other <see cref="IApplicationDbContext"/> member throws, since the tests never reach them.
/// </summary>
internal sealed class AuthDbContext : DbContext, IApplicationDbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options)
        : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<IntegrationConnection> IntegrationConnections => Set<IntegrationConnection>();
    public DbSet<IntegrationToken> IntegrationTokens => Set<IntegrationToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasKey(e => e.Id);
        b.Entity<User>().Ignore(e => e.Channel).Ignore(e => e.Pronoun);

        b.Entity<Channel>().HasKey(e => e.Id);
        b.Entity<Channel>().Ignore(e => e.Tags).Ignore(e => e.ContentLabels);
        b.Entity<Channel>()
            .Ignore(e => e.User)
            .Ignore(e => e.Moderators)
            .Ignore(e => e.Streams)
            .Ignore(e => e.Events);

        b.Entity<AuthSession>().HasKey(e => e.Id);
        b.Entity<AuthSession>()
            .Ignore(e => e.User)
            .Ignore(e => e.Channel)
            .Ignore(e => e.RefreshTokens);

        b.Entity<RefreshToken>().HasKey(e => e.Id);
        b.Entity<RefreshToken>().Ignore(e => e.Session).Ignore(e => e.User);

        b.Entity<IntegrationConnection>().HasKey(e => e.Id);
        b.Entity<IntegrationConnection>().Ignore(e => e.Channel).Ignore(e => e.Tokens);

        b.Entity<IntegrationToken>().HasKey(e => e.Id);
        b.Entity<IntegrationToken>().Ignore(e => e.Connection).Ignore(e => e.Channel);

        // EF discovers entity types from the DbSet<T> property declarations regardless of the throwing
        // getter bodies, then tries to map their jsonb-of-complex-type columns (unsupported on InMemory).
        // Ignore every entity these tests do not exercise so the model stays minimal and provider-agnostic.
        b.Ignore<NomNomzBot.Domain.Identity.Entities.ChannelModerator>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.Service>();
        b.Ignore<NomNomzBot.Domain.Commands.Entities.Command>();
        b.Ignore<NomNomzBot.Domain.Rewards.Entities.Reward>();
        b.Ignore<NomNomzBot.Domain.Widgets.Entities.Widget>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.EventSubSubscription>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.EventSubConduit>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.EventSubConduitShard>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.IdempotencyKey>();
        b.Ignore<NomNomzBot.Domain.Chat.Entities.ChatMessage>();
        b.Ignore<NomNomzBot.Domain.Identity.Entities.ChannelEvent>();
        b.Ignore<NomNomzBot.Domain.Stream.Entities.Stream>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.Configuration>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.Storage>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.Record>();
        b.Ignore<NomNomzBot.Domain.Identity.Entities.Permission>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.ChannelFeature>();
        b.Ignore<NomNomzBot.Domain.Identity.Entities.ChannelBotAuthorization>();
        b.Ignore<NomNomzBot.Domain.Identity.Entities.BotAccount>();
        b.Ignore<NomNomzBot.Domain.Identity.Entities.IpcDevModeKey>();
        b.Ignore<NomNomzBot.Domain.Discord.Entities.DiscordServerAuthorization>();
        b.Ignore<NomNomzBot.Domain.Identity.Entities.ChannelSubscription>();
        b.Ignore<NomNomzBot.Domain.Tts.Entities.TtsVoice>();
        b.Ignore<NomNomzBot.Domain.Tts.Entities.UserTtsVoice>();
        b.Ignore<NomNomzBot.Domain.Tts.Entities.TtsUsageRecord>();
        b.Ignore<NomNomzBot.Domain.Tts.Entities.TtsCacheEntry>();
        b.Ignore<NomNomzBot.Domain.Identity.Entities.Pronoun>();
        b.Ignore<NomNomzBot.Domain.Platform.Entities.DeletionAuditLog>();
        b.Ignore<NomNomzBot.Domain.Commands.Entities.Timer>();
        b.Ignore<NomNomzBot.Domain.Commands.Entities.EventResponse>();
        b.Ignore<NomNomzBot.Domain.Rewards.Entities.WatchStreak>();
        b.Ignore<NomNomzBot.Domain.Commands.Entities.Pipeline>();
        b.Ignore<NomNomzBot.Domain.EventStore.Entities.EventJournal>();
        b.Ignore<NomNomzBot.Domain.EventStore.Entities.TenantSequence>();
        b.Ignore<NomNomzBot.Domain.EventStore.Entities.ProjectionCheckpoint>();
    }

    // ── Unused IApplicationDbContext surface — never reached by these tests ──
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
    public DbSet<NomNomzBot.Domain.Identity.Entities.IpcDevModeKey> IpcDevModeKeys =>
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
    public DbSet<NomNomzBot.Domain.EventStore.Entities.EventJournal> EventJournals =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.EventStore.Entities.TenantSequence> TenantSequences =>
        throw new NotSupportedException();
    public DbSet<NomNomzBot.Domain.EventStore.Entities.ProjectionCheckpoint> ProjectionCheckpoints =>
        throw new NotSupportedException();
}
