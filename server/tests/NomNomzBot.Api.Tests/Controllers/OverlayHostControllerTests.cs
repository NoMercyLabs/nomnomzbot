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
/// Proves the overlay host shell serves at the exact URL shape the widgets API hands out and carries the
/// load-bearing wiring: the hub path, the token gate, the audio-bus targets, and the SignalR framing —
/// the pieces that make walk-in sounds audible in an OBS browser source.
/// </summary>
public sealed class OverlayHostControllerTests
{
    [Fact]
    public void Serves_the_browser_source_shell_with_the_audio_bus_wiring()
    {
        OverlayHostController sut = new();

        IActionResult result = sut.Get();

        ContentResult content = result.Should().BeOfType<ContentResult>().Subject;
        content.ContentType.Should().Be("text/html");
        content
            .Content.Should()
            .Contain("/hubs/overlay", "the shell must connect to the overlay hub")
            .And.Contain("PlaySound", "the audio bus must handle play")
            .And.Contain("StopSound", "the audio bus must handle stop")
            .And.Contain("JoinWidget", "a widgetId join must reach the widget group")
            .And.Contain("token", "the per-channel OverlayToken gates the hub connection")
            .And.Contain(
                "String.fromCharCode(30)",
                "SignalR JSON-protocol frames are record-separator delimited"
            );
    }
}
