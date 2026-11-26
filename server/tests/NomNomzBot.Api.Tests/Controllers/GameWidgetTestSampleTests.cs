// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Widgets.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The Test button on a game overlay plays that game's own round. The two game widgets share the event names
/// (game.lobby, game.running, game.resolved), so a sample keyed only by event name sent the drop game's frames to
/// the raffle overlay too, and it showed an empty card.
/// </summary>
public sealed class GameWidgetTestSampleTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly string[] GameEvents = ["game.lobby", "game.running", "game.resolved"];

    private static async Task<JsonElement> FireAsync(string widgetKey, string eventType)
    {
        await using ApiTestDbContext db = ApiTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        WidgetGalleryItem item = new() { Name = widgetKey, NaturalKey = widgetKey };
        Widget widget = new()
        {
            BroadcasterId = channel,
            Name = widgetKey,
            GalleryItemId = item.Id,
            IsEnabled = true,
            EventSubscriptions = [.. GameEvents],
        };
        db.WidgetGalleryItems.Add(item);
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();

        IWidgetNotifier notifier = Substitute.For<IWidgetNotifier>();
        WidgetTestEventController controller = new(
            db,
            notifier,
            Substitute.For<IOverlayPresenceRegistry>(),
            TimeProvider.System
        );
        await controller.Fire(
            channel.ToString(),
            widget.Id.ToString(),
            new WidgetTestEventRequest(eventType, null),
            CancellationToken.None
        );

        WidgetEventDto sent = notifier
            .ReceivedCalls()
            .Where(call =>
                call.GetMethodInfo().Name == nameof(IWidgetNotifier.SendWidgetEventAsync)
            )
            .Select(call => (WidgetEventDto)call.GetArguments()[2]!)
            .Single();
        return JsonSerializer.SerializeToElement(sent.Data, Json);
    }

    [Fact]
    public async Task A_raffle_test_fills_the_pot_and_draws_a_winner_from_the_entrants()
    {
        JsonElement lobby = await FireAsync("raffle", "game.lobby");
        JsonElement resolved = await FireAsync("raffle", "game.resolved");

        lobby.GetProperty("kind").GetString().Should().Be("join");
        lobby.GetProperty("entrants").GetArrayLength().Should().BeGreaterThan(1);
        lobby.GetProperty("pot").GetInt64().Should().BeGreaterThan(0);

        string? winner = resolved.GetProperty("winner").GetString();
        resolved
            .GetProperty("results")
            .EnumerateArray()
            .Should()
            .ContainSingle(r => r.GetProperty("won").GetBoolean())
            .Which.GetProperty("player")
            .GetString()
            .Should()
            .Be(winner);
    }

    [Fact]
    public async Task The_drop_game_keeps_its_own_target_round()
    {
        JsonElement frame = await FireAsync("drop_game", "game.lobby");

        frame.GetProperty("kind").GetString().Should().Be("round_open");
        frame.GetProperty("target").GetDouble().Should().BeGreaterThan(0);
    }
}
