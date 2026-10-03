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
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Infrastructure.DevPlatform;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// <c>GET /sdk/types.d.ts?context=script&amp;trigger=…</c> serves the script types for one trigger, so the editor
/// can flag a variable key that trigger never sets. An unknown trigger is a 400, not a silent untyped fallback.
/// </summary>
public sealed class SdkTypesTriggerTests
{
    private static SdkController Controller() =>
        new(new SdkTypeEmitter(new EventCatalog(), new OneSampleCatalog()));

    [Fact]
    public void A_known_trigger_returns_the_union_of_its_keys()
    {
        IActionResult result = Controller().GetTypes("script", "channel.follow");

        ContentResult content = result.Should().BeOfType<ContentResult>().Subject;
        content.ContentType.Should().Be("text/plain");
        content.Content.Should().Contain("type NnzVarKey = 'followed_at' | 'user';");
        content.Content.Should().Contain("getVar(key: string, dynamic: true)");
    }

    [Fact]
    public void An_unknown_trigger_is_a_400_with_the_error_envelope()
    {
        IActionResult result = Controller().GetTypes("script", "nope");

        BadRequestObjectResult bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        StatusResponseDto<object> body = bad
            .Value.Should()
            .BeOfType<StatusResponseDto<object>>()
            .Subject;
        body.Status.Should().Be("error");
        body.Code.Should().Be("UNKNOWN_TRIGGER");
    }

    [Fact]
    public void A_trigger_on_the_widget_context_is_a_400()
    {
        Controller()
            .GetTypes("widget", "channel.follow")
            .Should()
            .BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void No_trigger_keeps_the_untyped_getVar()
    {
        IActionResult result = Controller().GetTypes("script", null);

        result
            .Should()
            .BeOfType<ContentResult>()
            .Subject.Content.Should()
            .Contain("  getVar(key: string): string | null;");
    }

    private sealed class OneSampleCatalog : ITriggerSampleCatalog
    {
        private static readonly TriggerSample Follow = new(
            "channel.follow",
            "channel.follow",
            "42",
            "Viewer",
            new Dictionary<string, string> { ["user"] = "Viewer", ["followed_at"] = "t" }
        );

        public IReadOnlyList<TriggerSample> List() => [Follow];

        public TriggerSample? Find(string id) => id == Follow.Id ? Follow : null;
    }
}
