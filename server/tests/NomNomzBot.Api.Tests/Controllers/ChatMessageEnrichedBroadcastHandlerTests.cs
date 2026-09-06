// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Widgets.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the enrichment reaches an overlay as the shape the overlay actually reads. Every field on
/// <c>ChatMessageEnrichedEvent</c> is copied by hand into this handler's anonymous payload, and a field that
/// exists on the event but is missing here is invisible in C# — it simply never appears on the wire and the
/// renderer silently falls back. That is exactly how native GIF fragments shipped with no url, so this asserts
/// the SERIALIZED payload rather than the handler's inputs.
/// </summary>
public sealed class ChatMessageEnrichedBroadcastHandlerTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();

    private static JsonElement PayloadOf(WidgetEventDto pushed) =>
        JsonSerializer.SerializeToElement(
            pushed.Data,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
        );

    [Fact]
    public async Task A_song_request_enrichment_reaches_a_subscribing_widget_with_its_card_and_its_requester()
    {
        using ApiTestDbContext db = ApiTestDbContext.New();
        db.Widgets.Add(
            new Widget
            {
                BroadcasterId = Broadcaster,
                Name = "Twitch Chat Overlay",
                EventSubscriptions = ["ChatMessageEnriched"],
            }
        );
        await db.SaveChangesAsync();

        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        ChatMessageEnrichedBroadcastHandler handler = new(db, widgets);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Broadcaster,
                MessageId = "msg-1",
                LinkUrl = "https://open.spotify.com/track/abc",
                Title = "Out Of Time",
                Description = "The Rolling Stones",
                ImageUrl = "https://i.scdn.co/image/art.jpg",
                Provider = "spotify",
                UserDisplayName = "Kanawanagasaki",
                UserLogin = "kanawanagasaki",
            }
        );

        WidgetEventDto pushed = (WidgetEventDto)widgets.ReceivedCalls().Single().GetArguments()[2]!;
        pushed.EventType.Should().Be("ChatMessageEnriched");

        JsonElement payload = PayloadOf(pushed);

        // The card's own fields. Title and description are the track and the artist — an overlay that got one
        // without the other renders a half-named card, so both are asserted by value rather than presence.
        payload.GetProperty("title").GetString().Should().Be("Out Of Time");
        payload.GetProperty("description").GetString().Should().Be("The Rolling Stones");
        payload.GetProperty("imageUrl").GetString().Should().Be("https://i.scdn.co/image/art.jpg");
        payload
            .GetProperty("linkUrl")
            .GetString()
            .Should()
            .Be("https://open.spotify.com/track/abc");
        // Provider decides which mark the card wears; losing it renders neither Spotify nor YouTube.
        payload.GetProperty("provider").GetString().Should().Be("spotify");

        // Who the card belongs to. An overlay that hides command lines draws it as this viewer's own bubble and
        // has nothing else to name it with, so losing these makes every song request an anonymous card.
        payload.GetProperty("userDisplayName").GetString().Should().Be("Kanawanagasaki");
        payload.GetProperty("userLogin").GetString().Should().Be("kanawanagasaki");

        // The id ties the card to the line it enriches; a payload that kept the card but lost the id still looks
        // right on screen and can never be matched against an already-rendered message.
        payload.GetProperty("messageId").GetString().Should().Be("msg-1");
    }

    [Fact]
    public async Task A_widget_that_does_not_subscribe_to_enrichment_is_never_pushed_to()
    {
        // Subscription is what keeps chat-rate enrichment off widgets that never asked for it. A handler that
        // pushed to every widget on the channel would look identical to the test above.
        using ApiTestDbContext db = ApiTestDbContext.New();
        db.Widgets.Add(
            new Widget
            {
                BroadcasterId = Broadcaster,
                Name = "TTS Audio",
                EventSubscriptions = ["tts_speak"],
            }
        );
        await db.SaveChangesAsync();

        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        ChatMessageEnrichedBroadcastHandler handler = new(db, widgets);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Broadcaster,
                MessageId = "msg-2",
                Title = "Out Of Time",
                Provider = "spotify",
            }
        );

        widgets.ReceivedCalls().Should().BeEmpty();
    }
}
