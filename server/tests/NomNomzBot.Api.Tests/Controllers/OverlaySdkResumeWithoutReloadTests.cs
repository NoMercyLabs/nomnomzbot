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
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// After a dropped overlay hub connection (ticket exchange, WebSocket blip, bot restart) the SDK
/// reconnects and re-joins its widget group on its own — but a prior version of the JoinWidget
/// response handler forced a full <c>location.reload()</c> on every join AFTER the first, instead of
/// re-applying the fresh <c>initialState</c> it had just received. That meant a widget never actually
/// resumed in place: OBS silently reloaded the browser source (or worse, never regained a working page
/// if reload itself failed) rather than the SDK simply re-syncing state on the SAME page (S062b). The
/// SDK is inline script served as text, so this asserts on the served script itself — that is the only
/// place the behaviour exists.
/// </summary>
public sealed class OverlaySdkResumeWithoutReloadTests
{
    private static string Sdk()
    {
        OverlaySdkController controller = new()
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() },
        };
        ContentResult result = (ContentResult)controller.Get();
        return result.Content!;
    }

    [Fact]
    public void A_successful_reconnect_reapplies_initial_state_on_every_join_not_only_the_first()
    {
        string sdk = Sdk();

        int handshakeStart = sdk.IndexOf(
            "ws.onmessage = function (evt) {",
            StringComparison.Ordinal
        );
        handshakeStart.Should().BeGreaterThan(-1, "the SDK must handle inbound hub frames");

        int handshakeEnd = sdk.IndexOf(
            "ws.onclose = function ()",
            handshakeStart,
            StringComparison.Ordinal
        );
        handshakeEnd.Should().BeGreaterThan(handshakeStart);

        string messageHandler = sdk[handshakeStart..handshakeEnd];

        messageHandler
            .Should()
            .Contain(
                "applySettings(msg.result.initialState)",
                "every successful JoinWidget response — reconnect included — must re-sync the widget's real state"
            );

        messageHandler
            .Should()
            .NotContain(
                "location.reload()",
                "a resumed connection must NOT fall back to a full page reload to pick state back up"
            );
    }

    [Fact]
    public void The_reconnect_backoff_loop_keeps_retrying_without_ever_needing_a_manual_reload()
    {
        string sdk = Sdk();

        int closeStart = sdk.IndexOf("ws.onclose = function ()", StringComparison.Ordinal);
        closeStart
            .Should()
            .BeGreaterThan(-1, "a dropped socket must trigger the SDK's own reconnect");

        sdk[closeStart..]
            .Should()
            .Contain(
                "setTimeout(connect, backoffMs)",
                "onclose must re-invoke connect() itself so the page recovers without a manual reload"
            );
    }
}
