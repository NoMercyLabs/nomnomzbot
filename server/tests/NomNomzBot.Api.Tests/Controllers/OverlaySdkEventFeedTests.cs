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

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The generic overlay feed carries each event's data as JSON text (<c>OverlayEventDto.Payload</c>). The widget
/// must receive the parsed object: a handler reading <c>data.user</c> on a chat message got undefined while the
/// SDK passed the text through.
/// </summary>
public sealed class OverlaySdkEventFeedTests
{
    private const string ChatPayload = """{"user":"kitte","text":"hello chat"}""";

    [Fact]
    public void A_feed_event_reaches_its_handler_as_an_object()
    {
        OverlaySdkRuntime sdk = OverlaySdkRuntime.Connected();
        sdk.Evaluate("var got = null; NomNomz.on('ChatMessage', function (data) { got = data; });");

        sdk.Deliver("Event", new { type = "ChatMessage", payload = ChatPayload });

        sdk.Text("typeof got").Should().Be("object");
        sdk.Text("got.user").Should().Be("kitte");
        sdk.Text("got.text").Should().Be("hello chat");
    }

    [Fact]
    public void An_any_handler_gets_the_feed_event_name_and_the_parsed_object()
    {
        OverlaySdkRuntime sdk = OverlaySdkRuntime.Connected();
        sdk.Evaluate(
            "var seen = []; NomNomz.onAny(function (type, data) { seen.push(type + ':' + data.user); });"
        );

        sdk.Deliver("Event", new { type = "ChatMessage", payload = ChatPayload });

        sdk.Text("seen.join('|')").Should().Be("ChatMessage:kitte");
    }
}
