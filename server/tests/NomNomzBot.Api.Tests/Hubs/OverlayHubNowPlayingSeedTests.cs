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

using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Api.Hubs.Overlay;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// A widget that subscribes to <c>now_playing</c> gets the current song at once, in the shape of the live
/// push, so an overlay that loads between two songs is never empty.
/// </summary>
public sealed class OverlayHubNowPlayingSeedTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-000000000f31");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 15, 33, 0, TimeSpan.Zero);

    [Fact]
    public async Task Subscribing_to_now_playing_sends_the_current_song_to_that_page()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(Playing("Stoney_Eagle"));
        IMusicHistoryReader history = Substitute.For<IMusicHistoryReader>();
        history
            .ResolveRequestersAsync(
                Arg.Is<IReadOnlyCollection<string>>(names => names.Contains("Stoney_Eagle")),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new Dictionary<string, RequesterIdentityDto>
                {
                    ["Stoney_Eagle"] = new("141981764", "#ff7f50"),
                }
            );

        List<WidgetEventDto> sent = [];
        Guid widgetId = await SubscribeAsync(music, history, ["now_playing"], sent);

        sent.Should().ContainSingle();
        sent[0].WidgetId.Should().Be(widgetId.ToString());
        sent[0].EventType.Should().Be("now_playing");
        sent[0]
            .Data.Should()
            .Be(
                new NowPlayingWidgetPayload(
                    true,
                    "The A Team",
                    "Ed Sheeran",
                    "https://art.example/a-team.jpg",
                    "spotify",
                    "spotify:track:VFC9",
                    258_000,
                    12_000,
                    Now,
                    "Stoney_Eagle",
                    "#ff7f50"
                )
            );
    }

    [Fact]
    public async Task A_page_that_does_not_subscribe_to_now_playing_gets_no_song()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(Playing(null));

        List<WidgetEventDto> sent = [];
        await SubscribeAsync(music, Substitute.For<IMusicHistoryReader>(), ["ChatMessage"], sent);

        sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Nothing_is_sent_when_no_song_is_known()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns((NowPlaying?)null);

        List<WidgetEventDto> sent = [];
        await SubscribeAsync(music, Substitute.For<IMusicHistoryReader>(), ["now_playing"], sent);

        sent.Should().BeEmpty();
    }

    private static NowPlaying Playing(string? requestedBy) =>
        new(
            "The A Team",
            "Ed Sheeran",
            "+",
            "https://art.example/a-team.jpg",
            258_000,
            12_000,
            true,
            80,
            requestedBy,
            "spotify",
            "spotify:track:VFC9"
        );

    private static async Task<Guid> SubscribeAsync(
        IMusicService music,
        IMusicHistoryReader history,
        string[] events,
        List<WidgetEventDto> sent
    )
    {
        using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid widgetId = Guid.CreateVersion7();
        db.Widgets.Add(
            new()
            {
                Id = widgetId,
                BroadcasterId = Broadcaster,
                Name = "Spotify NowPlaying",
                IsEnabled = true,
                Settings = new(),
            }
        );
        await db.SaveChangesAsync();

        IWidgetService widgetService = Substitute.For<IWidgetService>();
        widgetService
            .GetEffectiveSettingsAsync(Broadcaster, widgetId, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new Dictionary<string, object>()));

        OverlayTicketService tickets = new(new FakeTimeProvider());
        HubCallerContext context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("now-playing-conn");
        context.Items.Returns(new Dictionary<object, object?>());
        DefaultHttpContext http = new();
        http.Request.QueryString = new(
            "?ticket="
                + Uri.EscapeDataString(
                    tickets.IssueTicket(new OverlayTokenScope(Broadcaster, widgetId))
                )
        );
        context.GetHttpContext().Returns(http);

        IOverlayClient caller = Substitute.For<IOverlayClient>();
        caller.WidgetEvent(Arg.Do<WidgetEventDto>(sent.Add)).Returns(Task.CompletedTask);
        IHubCallerClients<IOverlayClient> clients = Substitute.For<
            IHubCallerClients<IOverlayClient>
        >();
        clients.Caller.Returns(caller);

        OverlayHub hub = new(
            db,
            widgetService,
            tickets,
            new(),
            Substitute.For<IChannelRegistry>(),
            Substitute.For<IActionRequiredChangeNotifier>(),
            Substitute.For<IEventBus>(),
            [],
            NullLogger<OverlayHub>.Instance,
            nowPlaying: new(music, history, new FakeTimeProvider(Now))
        )
        {
            Context = context,
            Groups = Substitute.For<IGroupManager>(),
            Clients = clients,
        };
        await hub.OnConnectedAsync();
        await hub.JoinWidgetWithSdk(widgetId.ToString(), "test");

        bool accepted = await hub.Subscribe(widgetId.ToString(), events);

        accepted.Should().BeTrue();
        return widgetId;
    }
}
