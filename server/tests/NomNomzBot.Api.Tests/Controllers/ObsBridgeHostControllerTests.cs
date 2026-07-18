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
/// Proves the OBS-control bridge browser source serves at <c>/obs-bridge</c> and carries the load-bearing
/// wiring the bridge cannot function without: the relay hub path + token gate, the SignalR JSON framing, the
/// local OBS/VTS sockets, and the <c>OBSRelayHub</c> method names (<c>ExecuteObsRequest</c> in,
/// <c>AckCommand</c>/<c>ForwardObsEvent</c>/<c>ForwardVtsEvent</c> out) — a dropped marker would leave the
/// pasted bridge URL dark and the dashboard's bridge status stuck offline.
/// </summary>
public sealed class ObsBridgeHostControllerTests
{
    [Fact]
    public void Serves_the_bridge_source_shell_with_the_relay_wiring()
    {
        ObsBridgeHostController sut = new();

        IActionResult result = sut.Get();

        ContentResult content = result.Should().BeOfType<ContentResult>().Subject;
        content.ContentType.Should().Be("text/html");
        content
            .Content.Should()
            .Contain("/hubs/obs", "the bridge connects to the OBS relay hub")
            .And.Contain("token", "the per-channel BridgeToken gates the hub connection")
            .And.Contain(
                "String.fromCharCode(30)",
                "SignalR JSON-protocol frames are record-separator delimited"
            )
            .And.Contain("ExecuteObsRequest", "the bridge receives command pushes from the leader")
            .And.Contain("AckCommand", "each command is acked by id");
    }

    [Fact]
    public void Drives_both_the_obs_and_vts_local_legs()
    {
        ObsBridgeHostController sut = new();

        ContentResult content = sut.Get().Should().BeOfType<ContentResult>().Subject;

        content
            .Content.Should()
            .Contain("ws://127.0.0.1:4455", "the OBS leg dials the local OBS-WebSocket v5 endpoint")
            .And.Contain(
                "ForwardObsEvent",
                "subscribed OBS events relay back for the trigger surface"
            )
            .And.Contain("vts_request", "the VTS payload kind is dispatched to the VTS leg")
            .And.Contain("ws://localhost:8001", "the VTS leg dials the local VTube Studio endpoint")
            .And.Contain("VTubeStudioPublicAPI", "the VTS envelope mirrors DirectVtsTransport")
            .And.Contain(
                "ForwardVtsEvent",
                "subscribed VTS events relay back for the trigger surface"
            )
            .And.Contain("textContent", "debug text is inserted as text nodes only, never markup");
    }
}
