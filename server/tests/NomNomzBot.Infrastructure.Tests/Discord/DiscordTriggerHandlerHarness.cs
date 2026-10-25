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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Domain.Discord.Entities;
using NomNomzBot.Domain.Discord.ValueObjects;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Discord;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Discord;

/// <summary>
/// Runs a Discord trigger handler against the REAL dispatcher, template engine and dedupe constraint on a
/// SQLite database, with only the Discord gateway recorded. A test raises the domain event and reads back what
/// reached Discord (channel, rendered text) and what the dispatch log holds (dedupe key, status).
/// </summary>
internal sealed class DiscordTriggerHandlerHarness : IDisposable
{
    public const string DiscordChannel = "discord-chan-1";

    private static readonly FakeTimeProvider Clock = new(new(2026, 6, 22, 18, 0, 0, TimeSpan.Zero));

    private readonly DiscordSqliteTestDatabase _database;

    private DiscordTriggerHandlerHarness(DiscordSqliteTestDatabase database, Guid broadcasterId)
    {
        _database = database;
        BroadcasterId = broadcasterId;
    }

    public Guid BroadcasterId { get; }

    public RecordingGateway Gateway { get; } = new();

    /// <summary>A channel with an active Discord link and ONE rule for <paramref name="trigger"/>.</summary>
    public static async Task<DiscordTriggerHandlerHarness> WithRuleAsync(
        string trigger,
        string template,
        DiscordEmbedConfig? embed = null
    )
    {
        DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await DiscordTestHarness.SeedChannelAsync(database);
        Guid connectionId = await DiscordTestHarness.SeedActiveConnectionAsync(database, channel);
        await DiscordTestHarness.SeedConfigAsync(
            database,
            channel,
            connectionId,
            template,
            DiscordChannel,
            triggerType: trigger,
            embed: embed
        );
        return new(database, channel);
    }

    /// <summary>A channel with an active Discord link and no rule at all.</summary>
    public static async Task<DiscordTriggerHandlerHarness> WithoutRuleAsync()
    {
        DiscordSqliteTestDatabase database = DiscordSqliteTestDatabase.Open();
        Guid channel = await DiscordTestHarness.SeedChannelAsync(database);
        await DiscordTestHarness.SeedActiveConnectionAsync(database, channel);
        return new(database, channel);
    }

    /// <summary>Puts the channel on a platform under a login, and gives its owner a profile picture.</summary>
    public async Task ShapeChannelAsync(string provider, string login, string? profileImageUrl)
    {
        await using DiscordTestDbContext db = _database.NewContext();
        Channel channel = await db.Channels.SingleAsync(c => c.Id == BroadcasterId);
        channel.Provider = provider;
        channel.Name = login;
        channel.NameNormalized = login.ToLowerInvariant();
        db.Users.Add(
            new()
            {
                Id = channel.OwnerUserId,
                Username = login,
                UsernameNormalized = login.ToLowerInvariant(),
                DisplayName = login,
                ProfileImageUrl = profileImageUrl,
            }
        );
        await db.SaveChangesAsync();
    }

    /// <summary>Hands the event to a handler built over the real dispatcher.</summary>
    public async Task RaiseAsync<TEvent>(
        Func<IDiscordNotificationDispatcher, IEventHandler<TEvent>> createHandler,
        TEvent @event
    )
        where TEvent : class, IDomainEvent
    {
        await using DiscordTestDbContext db = _database.NewContext();
        DiscordGuildService guildService = new(
            db,
            new RecordingVault(),
            new DiscordTestUnitOfWork(db),
            new RecordingEventBus(),
            Clock,
            new PipelineStepReferenceScanner(db)
        );
        DiscordNotificationDispatcher dispatcher = new(
            db,
            guildService,
            Gateway,
            DiscordTemplateTestSupport.CreateResolver(),
            new RecordingEventBus(),
            Clock
        );
        await createHandler(dispatcher).HandleAsync(@event);
    }

    /// <summary>Every dispatch row written so far, oldest first.</summary>
    public async Task<List<DiscordNotificationDispatch>> DispatchesAsync()
    {
        await using DiscordTestDbContext db = _database.NewContext();
        return await db
            .DiscordNotificationDispatches.OrderBy(row => row.DispatchedAt)
            .ToListAsync();
    }

    public void Dispose() => _database.Dispose();
}
