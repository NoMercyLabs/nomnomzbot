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
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Widgets.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The editor preview fired its events from a hand-kept Kotlin copy of the server's samples, and that copy
/// drifted from what the live events send. The server now hands out the one table it fires from itself.
/// </summary>
public sealed class WidgetTestEventSamplesTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static Dictionary<string, object> Samples()
    {
        using ApiTestDbContext db = ApiTestDbContext.New();
        WidgetTestEventController controller = new(
            db,
            Substitute.For<IWidgetNotifier>(),
            Substitute.For<IOverlayPresenceRegistry>(),
            TimeProvider.System
        );
        IActionResult result = controller.Samples(Guid.CreateVersion7().ToString());
        return ((StatusResponseDto<Dictionary<string, object>>)((ObjectResult)result).Value!).Data!;
    }

    [Fact]
    public void Every_event_type_the_Test_button_can_fire_has_its_sample_in_the_table()
    {
        Dictionary<string, object> samples = Samples();

        samples.Keys.Should().Contain(WidgetTestSamples.EventTypes);
        samples.Should().ContainKey("_default");
    }

    [Fact]
    public void A_sample_in_the_table_carries_the_fields_the_live_event_sends()
    {
        // The Kotlin copy's follow had only "user"; the live follow also carries login, avatar and standing.
        JsonElement follow = JsonSerializer.SerializeToElement(Samples()["follow"], Json);

        follow.GetProperty("login").GetString().Should().NotBeNullOrEmpty();
        follow.GetProperty("avatarUrl").GetString().Should().StartWith("https://");
        follow.GetProperty("communityStanding").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void An_unknown_event_falls_back_to_the_same_payload_the_server_fires_for_it()
    {
        JsonElement fallback = JsonSerializer.SerializeToElement(Samples()["_default"], Json);
        JsonElement fired = JsonSerializer.SerializeToElement(
            WidgetTestSamples.For("__nothing_declares_this__", DateTimeOffset.UnixEpoch),
            Json
        );

        fallback.GetRawText().Should().Be(fired.GetRawText());
    }
}
