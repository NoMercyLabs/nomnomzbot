// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the overlay host serves each widget as its OWN standalone SPA: the widget's live settings + id +
/// subscriptions are injected as globals, its compiled bundle mounts directly into <c>#app</c> after the runtime
/// and SDK, and there is NO shared shell and NO iframe (the architecture the owner rejected). Bad token / unknown
/// widget degrade to a transparent placeholder, and an injected settings value cannot break out of its script.
/// </summary>
public sealed class OverlayHostControllerTests
{
    private static readonly Guid WidgetId = new("11111111-1111-1111-1111-111111111111");
    private const string Token = "overlay-token";

    private static OverlayHostController WithManifest(params OverlayWidgetEntry[] widgets)
    {
        IWidgetService service = Substitute.For<IWidgetService>();
        OverlayManifest manifest = new(
            Guid.NewGuid(),
            "nonce",
            new List<OverlayWidgetEntry>(widgets)
        );
        service
            .GetOverlayManifestAsync(Token, Arg.Any<CancellationToken>())
            .Returns(Result<OverlayManifest>.Success(manifest));
        return new OverlayHostController(service);
    }

    private static OverlayWidgetEntry VueEntry(Dictionary<string, object?> settings) =>
        new(
            WidgetId,
            "Chat Box",
            "vue",
            "unverified",
            $"/api/v1/overlay/bundle/{WidgetId}",
            "hash123",
            new List<string> { "twitch.chat.message" },
            settings
        );

    private static async Task<string> BodyOf(IActionResult result)
    {
        ContentResult content = result.Should().BeOfType<ContentResult>().Subject;
        content.ContentType.Should().StartWith("text/html");
        await Task.CompletedTask;
        return content.Content ?? string.Empty;
    }

    [Fact]
    public async Task Serves_a_vue_widget_as_a_standalone_spa_with_its_injected_config()
    {
        OverlayHostController sut = WithManifest(
            VueEntry(
                new Dictionary<string, object?> { ["maxMessages"] = 5, ["accentColor"] = "#abcdef" }
            )
        );

        string html = await BodyOf(
            await sut.Get(WidgetId.ToString(), Token, CancellationToken.None)
        );

        // A real per-widget SPA: its own mount root, the runtime + SDK, and the widget's own bundle.
        html.Should()
            .Contain("id=\"app\"", "the widget mounts into its own root, not a shared shell")
            .And.Contain("/overlay/vue.js", "the Vue runtime loads before the bundle")
            .And.Contain("/overlay/sdk.js", "the SDK opens the widget's own hub connection")
            .And.Contain(
                $"/api/v1/overlay/bundle/{WidgetId}",
                "the widget serves its OWN compiled bundle"
            );

        // The config is injected server-side (this is the mechanism the owner asked for).
        html.Should()
            .Contain("window.WIDGET_SETTINGS", "settings are injected as a global")
            .And.Contain("\"maxMessages\":5", "the actual saved settings are injected verbatim")
            .And.Contain("\"accentColor\":\"#abcdef\"")
            .And.Contain("window.WIDGET_EVENT_SUBSCRIPTIONS")
            .And.Contain("twitch.chat.message", "the widget's declared subscriptions are injected")
            .And.Contain(
                $"window.WIDGET_ID=\"{WidgetId}\"",
                "the widget id is injected as a string literal"
            );

        // The rejected architecture must be gone.
        html.Should()
            .NotContain("<iframe", "the widget is a real SPA, never a sandboxed iframe")
            .And.NotContain("alert-box", "no baked-in shared alert surface")
            .And.NotContain("hype-train", "no baked-in shared hype-train surface")
            .And.NotContain(
                "postMessage",
                "the SDK talks to the hub directly, not via a host bridge"
            );
    }

    [Fact]
    public async Task Missing_token_serves_a_placeholder_not_a_widget()
    {
        OverlayHostController sut = WithManifest();

        string html = await BodyOf(
            await sut.Get(WidgetId.ToString(), token: null, CancellationToken.None)
        );

        html.Should()
            .NotContain("/overlay/sdk.js")
            .And.NotContain("id=\"app\"")
            .And.Contain("token", "the placeholder explains the missing token");
    }

    [Fact]
    public async Task Unknown_or_disabled_widget_serves_a_placeholder()
    {
        // Manifest resolves (valid token) but lists no widget with this id.
        OverlayHostController sut = WithManifest();

        string html = await BodyOf(
            await sut.Get(WidgetId.ToString(), Token, CancellationToken.None)
        );

        html.Should().NotContain("/overlay/sdk.js").And.NotContain($"bundle/{WidgetId}");
    }

    [Fact]
    public async Task Injected_settings_cannot_break_out_of_the_script()
    {
        OverlayHostController sut = WithManifest(
            VueEntry(
                new Dictionary<string, object?> { ["evil"] = "</script><script>alert(1)</script>" }
            )
        );

        string html = await BodyOf(
            await sut.Get(WidgetId.ToString(), Token, CancellationToken.None)
        );

        html.Should()
            .NotContain(
                "</script><script>alert",
                "a raw </script></script><script> in a setting would break out of the injected block"
            )
            .And.NotContain(
                "<script>alert(1)",
                "the value's markup is neutralized (HTML-safe JSON encoder), not left as live tags"
            )
            .And.Contain("alert(1)", "the setting value itself is preserved, just escaped");
    }
}
