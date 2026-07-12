// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.PickLists.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.PickLists.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.PickLists;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.PickLists;

/// <summary>
/// Proves the <c>{list.pick.&lt;name&gt;}</c> template variable end to end through the real <see cref="TemplateResolver"/>
/// over the real <see cref="PickListService"/>: it resolves to one of the list's entries, a placeholder INSIDE the
/// picked entry (e.g. <c>{user}</c>) is itself resolved in the same pass, and an unknown list name resolves to an
/// empty string without throwing.
/// </summary>
public sealed class PickListTemplateResolverTests : IDisposable
{
    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-9000-00000000d001");

    private readonly PickListSqliteTestDatabase _database;
    private readonly PickListTestDbContext _db;
    private readonly TemplateResolver _resolver;

    public PickListTemplateResolverTests()
    {
        _database = PickListSqliteTestDatabase.Open();
        _db = _database.NewContext();

        _db.Channels.Add(
            new Channel
            {
                Id = Channel,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "555",
                Name = "streamer",
                NameNormalized = "streamer",
            }
        );
        _db.PickLists.Add(
            new PickList
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Channel,
                Name = "fight_moves",
                Items = ["punch", "kick", "headbutt"],
            }
        );
        _db.PickLists.Add(
            new PickList
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Channel,
                Name = "attacks",
                Items = ["{user} bonks {target}"],
            }
        );
        _db.SaveChanges();

        // The resolver resolves IPickListService from a fresh scope per call; register the focused SQLite context
        // as the singleton IApplicationDbContext the scoped service reads through (mirrors the pronoun-grammar test).
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(_db);
        services.AddSingleton<IEventBus, RecordingEventBus>();
        services.AddScoped<IPickListService, PickListService>();
        ServiceProvider provider = services.BuildServiceProvider();

        _resolver = new TemplateResolver(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        );
    }

    [Fact]
    public async Task ListPick_ResolvesToOneOfTheListEntries()
    {
        string resolved = await _resolver.ResolveAsync(
            "You {list.pick.fight_moves} them!",
            new Dictionary<string, string>(),
            Channel
        );

        resolved.Should().BeOneOf("You punch them!", "You kick them!", "You headbutt them!");
    }

    [Fact]
    public async Task ListPick_ResolvesPlaceholdersInsideThePickedEntry()
    {
        Dictionary<string, string> seeds = new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = "alice",
            ["target"] = "bob",
        };

        string resolved = await _resolver.ResolveAsync("{list.pick.attacks}", seeds, Channel);

        // The single entry "{user} bonks {target}" gets its own placeholders resolved in the same pass.
        resolved.Should().Be("alice bonks bob");
    }

    [Fact]
    public async Task ListPick_UnknownList_ResolvesToEmptyString_WithoutThrowing()
    {
        string resolved = await _resolver.ResolveAsync(
            "A[{list.pick.nonexistent}]B",
            new Dictionary<string, string>(),
            Channel
        );

        // A missing list expands to empty — the surrounding text survives and nothing throws.
        resolved.Should().Be("A[]B");
    }

    public void Dispose()
    {
        _db.Dispose();
        _database.Dispose();
    }
}
