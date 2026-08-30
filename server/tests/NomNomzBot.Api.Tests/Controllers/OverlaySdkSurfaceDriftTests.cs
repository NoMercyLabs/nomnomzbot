// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Api.Controllers;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DevPlatform;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure.DevPlatform;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The drift guard for the widget context's authored globals (<c>SdkRuntimeSurface.WidgetGlobals</c>). A widget
/// page's whole SDK is the object <c>OverlaySdkController</c> assigns to <c>window.NomNomz</c> plus the config
/// <c>OverlayHostController</c> injects — so this reads BOTH from the real served artifacts and holds the generated
/// widget <c>nnz.d.ts</c> to exactly that, in both directions. Without it the widget types drifted all the way into
/// fiction: they declared an <c>nnz</c> global (batteries + api) that has never existed in a browser and declared
/// nothing at all for <c>NomNomz</c>, which is why every first-party widget hand-writes
/// <c>const nnz = (window as any).NomNomz</c>.
/// </summary>
public sealed partial class OverlaySdkSurfaceDriftTests
{
    private static readonly Guid WidgetId = new("11111111-1111-1111-1111-111111111111");
    private const string Token = "overlay-token";

    private static string WidgetDts() =>
        new SdkTypeEmitter(new EventCatalog()).EmitTypeScript(SdkContext.Widget);

    /// <summary>The overlay SDK exactly as a widget page downloads it.</summary>
    private static string ServedSdk()
    {
        OverlaySdkController controller = new()
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() },
        };
        ContentResult content = controller.Get().Should().BeOfType<ContentResult>().Subject;
        return content.Content ?? string.Empty;
    }

    /// <summary>A real rendered widget page, so the injected config globals come from the renderer, not a list.</summary>
    private static async Task<string> RenderedWidgetPage()
    {
        OverlayWidgetEntry entry = new(
            WidgetId,
            "Chat Box",
            "vue",
            "unverified",
            $"/api/v1/overlay/bundle/{WidgetId}",
            "hash123",
            ["twitch.chat.message"],
            new Dictionary<string, object?> { ["accentColor"] = "#abcdef" }
        );
        IWidgetService service = Substitute.For<IWidgetService>();
        service
            .GetOverlayManifestAsync(Token, Arg.Any<CancellationToken>())
            .Returns(Result<OverlayManifest>.Success(new(Guid.NewGuid(), "nonce", [entry])));

        IActionResult result = await new OverlayHostController(service).Get(
            WidgetId.ToString(),
            Token,
            CancellationToken.None
        );
        return result.Should().BeOfType<ContentResult>().Subject.Content ?? string.Empty;
    }

    // ── Runtime readers ─────────────────────────────────────────────────────

    /// <summary>The top-level keys of the object literal the SDK assigns to <c>window.NomNomz</c>.</summary>
    private static List<string> SdkObjectMembers(string sdkJs) =>
        BlockMembers(sdkJs, ApiLiteralStart(), JsMember());

    /// <summary>The name the SDK actually installs the object under (<c>window.NomNomz</c>).</summary>
    private static string SdkGlobalName(string sdkJs)
    {
        Match match = SdkGlobalAssignment().Match(sdkJs);
        match.Success.Should().BeTrue("the SDK has to install itself on some window property");
        return match.Groups[1].Value;
    }

    /// <summary>Every <c>window.SCREAMING_CASE = …</c> the host page injects before the bundle runs.</summary>
    private static List<string> InjectedPageGlobals(string html) =>
        [
            .. InjectedGlobal()
                .Matches(html)
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal),
        ];

    // ── Declaration readers ─────────────────────────────────────────────────

    private static List<string> DeclaredSdkMembers(string dts) =>
        BlockMembers(dts, SdkInterfaceStart(), TsMember());

    private static List<string> DeclaredPageGlobals(string dts) =>
        [
            .. DeclaredConst()
                .Matches(dts)
                .Select(m => m.Groups[1].Value)
                .OrderBy(n => n, StringComparer.Ordinal),
        ];

    /// <summary>
    /// Top-level member names of the first brace block <paramref name="start"/> opens. Brace depth keeps a nested
    /// literal's members out, so only the block's own surface is returned.
    /// </summary>
    private static List<string> BlockMembers(string source, Regex start, Regex member)
    {
        List<string> members = [];
        bool inside = false;
        int depth = 0;

        foreach (string line in source.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (!inside)
            {
                if (!start.IsMatch(line))
                    continue;
                inside = true;
                depth = 1;
                continue;
            }

            if (depth == 1)
            {
                Match hit = member.Match(line);
                if (hit.Success)
                    members.Add(hit.Groups[1].Value);
            }

            depth += line.Count(c => c == '{') - line.Count(c => c == '}');
            if (depth <= 0)
                break;
        }
        return members;
    }

    [GeneratedRegex(@"^\s*var api = \{\s*$")]
    private static partial Regex ApiLiteralStart();

    [GeneratedRegex(@"^\s*(?:get\s+|set\s+)?([A-Za-z_$][A-Za-z0-9_$]*)\s*[:(]")]
    private static partial Regex JsMember();

    [GeneratedRegex(@"window\.([A-Za-z_$][A-Za-z0-9_$]*)\s*=\s*api\s*;")]
    private static partial Regex SdkGlobalAssignment();

    [GeneratedRegex(@"window\.([A-Z][A-Z0-9_]*)\s*=")]
    private static partial Regex InjectedGlobal();

    [GeneratedRegex(@"^interface NnzOverlaySdk \{\s*$")]
    private static partial Regex SdkInterfaceStart();

    [GeneratedRegex(@"^  (?:readonly\s+)?([A-Za-z_$][A-Za-z0-9_$]*)\s*[:(<]")]
    private static partial Regex TsMember();

    [GeneratedRegex(@"^declare const ([A-Z][A-Z0-9_]*):", RegexOptions.Multiline)]
    private static partial Regex DeclaredConst();

    // ── The guard ───────────────────────────────────────────────────────────

    [Fact]
    public void Widget_dts_declares_exactly_the_members_the_served_overlay_sdk_exposes()
    {
        List<string> runtime = SdkObjectMembers(ServedSdk());
        List<string> declared = DeclaredSdkMembers(WidgetDts());

        // Sanity: the literal really was found, so an empty-vs-empty comparison can never pass by accident.
        runtime.Should().Contain("on").And.Contain("settings");

        List<string> undeclared = [.. runtime.Except(declared, StringComparer.Ordinal)];
        List<string> phantom = [.. declared.Except(runtime, StringComparer.Ordinal)];

        undeclared
            .Should()
            .BeEmpty(
                "window.NomNomz exposes members the widget .d.ts never declares, so the editor hides them — "
                    + "undeclared: "
                    + string.Join(", ", undeclared)
            );
        phantom
            .Should()
            .BeEmpty(
                "the widget .d.ts declares members window.NomNomz does not have, so autocomplete leads straight "
                    + "into a TypeError — phantom: "
                    + string.Join(", ", phantom)
            );
    }

    [Fact]
    public void Widget_dts_declares_the_sdk_under_the_name_the_sdk_installs_it_as()
    {
        string globalName = SdkGlobalName(ServedSdk());

        globalName.Should().Be("NomNomz");
        WidgetDts().Should().Contain($"declare const {globalName}: NnzOverlaySdk;");
        // The Jint sandbox's `nnz` is server-side only; declaring it here is what sent widget authors hunting.
        WidgetDts().Should().NotContain("declare const nnz");
    }

    [Fact]
    public async Task Widget_dts_declares_exactly_the_config_globals_the_host_page_injects()
    {
        List<string> injected = InjectedPageGlobals(await RenderedWidgetPage());
        List<string> declared = DeclaredPageGlobals(WidgetDts());

        injected
            .Should()
            .Equal(
                "WIDGET_EVENT_SUBSCRIPTIONS",
                "WIDGET_ID",
                "WIDGET_NAME",
                "WIDGET_SETTINGS",
                "WIDGET_TOKEN"
            );
        declared.Should().Equal(injected);
    }
}
