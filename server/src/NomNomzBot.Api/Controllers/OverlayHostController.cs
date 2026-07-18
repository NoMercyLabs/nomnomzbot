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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Api.Controllers;

/// <summary>
/// Serves the overlay page an OBS browser source loads, at the URL the widgets API hands out
/// (<c>/overlay?widgetId={id}&amp;token={overlayToken}</c>). Each widget gets its OWN standalone page: the server
/// injects that widget's config as globals (<c>window.WIDGET_SETTINGS/ID/EVENT_SUBSCRIPTIONS</c>) and the widget's
/// compiled bundle mounts DIRECTLY into <c>#app</c> as a real SPA (no shared shell, no iframe). The overlay SDK
/// (<c>/overlay/sdk.js</c>) opens the widget's own hub connection, reads the injected settings, and delivers the
/// channel's subscription-matched events. A <c>vanilla</c> widget's bundle is browser-ready HTML — the config +
/// SDK are injected into it; a framework (vue/react/…) bundle is a self-mounting module loaded after its runtime.
/// The page carries the overlay token (it gates the hub) but no user secrets, and is served anonymously.
/// </summary>
[ApiController]
[Route("overlay")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class OverlayHostController : ControllerBase
{
    private readonly IWidgetService _widgetService;

    public OverlayHostController(IWidgetService widgetService)
    {
        _widgetService = widgetService;
    }

    /// <summary>The per-widget browser-source page. <c>token</c> gates the hub; <c>widgetId</c> selects the widget.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? widgetId,
        [FromQuery] string? token,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(token))
            return Content(
                Placeholder("This overlay URL is missing its token."),
                "text/html; charset=utf-8"
            );

        if (string.IsNullOrWhiteSpace(widgetId))
            return Content(
                Placeholder("Add ?widgetId= to load a widget."),
                "text/html; charset=utf-8"
            );

        Result<OverlayManifest> manifest = await _widgetService.GetOverlayManifestAsync(token, ct);
        if (manifest.IsFailure)
            return Content(
                Placeholder("This overlay token is not valid."),
                "text/html; charset=utf-8"
            );

        OverlayWidgetEntry? entry = manifest.Value.Widgets.FirstOrDefault(w =>
            string.Equals(w.WidgetId.ToString(), widgetId, StringComparison.OrdinalIgnoreCase)
        );
        if (entry is null)
            return Content(
                Placeholder(
                    "That widget is not live on this channel (unknown, disabled, or not built)."
                ),
                "text/html; charset=utf-8"
            );

        // A vanilla widget's bundle IS the page's HTML — inject config + SDK into it. A framework widget is a
        // self-mounting module the page loads after its runtime.
        if (string.Equals(entry.Framework, "vanilla", StringComparison.OrdinalIgnoreCase))
        {
            Result<OverlayBundle> bundle = await _widgetService.GetOverlayBundleAsync(
                token,
                widgetId,
                ct
            );
            if (bundle.IsFailure)
                return Content(
                    Placeholder("This widget has not been built yet."),
                    "text/html; charset=utf-8"
                );
            return Content(
                RenderVanillaPage(entry, bundle.Value.Content, token),
                "text/html; charset=utf-8"
            );
        }

        return Content(RenderFrameworkPage(entry, token), "text/html; charset=utf-8");
    }

    // ── Page rendering ──────────────────────────────────────────────────────

    /// <summary>The injected config block — the widget's live settings, id, name, and declared event subscriptions.</summary>
    private static string ConfigScript(OverlayWidgetEntry entry, string token) =>
        $$"""
            <script>
            window.WIDGET_ID={{JsLiteral(entry.WidgetId.ToString())}};
            window.WIDGET_TOKEN={{JsLiteral(token)}};
            window.WIDGET_NAME={{JsLiteral(entry.Name)}};
            window.WIDGET_SETTINGS={{JsLiteral(entry.Settings)}};
            window.WIDGET_EVENT_SUBSCRIPTIONS={{JsLiteral(entry.EventSubscriptions)}};
            </script>
            """;

    /// <summary>A framework (vue/react/…) widget: config + runtime + SDK, then the self-mounting bundle into #app.</summary>
    private static string RenderFrameworkPage(OverlayWidgetEntry entry, string token)
    {
        string runtimeTag = string.Equals(
            entry.Framework,
            "vue",
            StringComparison.OrdinalIgnoreCase
        )
            ? "<script src=\"/overlay/vue.js\"></script>"
            : string.Empty;
        string bundleUrl =
            $"/api/v1/overlay/bundle/{entry.WidgetId}?token={Uri.EscapeDataString(token)}&v={entry.ContentHash}";

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <title>NomNomzBot Overlay</title>
            <style>html,body{margin:0;padding:0;background:transparent;overflow:hidden}#app{position:fixed;inset:0}</style>
            {{ConfigScript(entry, token)}}
            {{runtimeTag}}
            <script src="/overlay/sdk.js"></script>
            </head>
            <body>
            <div id="app"><script src="{{bundleUrl}}"></script></div>
            </body>
            </html>
            """;
    }

    /// <summary>A vanilla widget: its bundle is complete HTML — splice the config + SDK in before the app runs.</summary>
    private static string RenderVanillaPage(
        OverlayWidgetEntry entry,
        string bundleHtml,
        string token
    )
    {
        string inject = ConfigScript(entry, token) + "\n<script src=\"/overlay/sdk.js\"></script>";
        if (bundleHtml.Contains("</head>", StringComparison.OrdinalIgnoreCase))
            return ReplaceFirst(bundleHtml, "</head>", inject + "</head>");
        if (bundleHtml.Contains("<body>", StringComparison.OrdinalIgnoreCase))
            return ReplaceFirst(bundleHtml, "<body>", "<body>" + inject);
        // No head/body to splice into — prepend so the globals + SDK still exist before the widget's own scripts.
        return inject + bundleHtml;
    }

    /// <summary>A transparent fallback page for the error cases (still an OBS-safe transparent surface).</summary>
    private static string Placeholder(string message) =>
        $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8"><title>NomNomzBot Overlay</title>
            <style>html,body{margin:0;background:transparent;overflow:hidden;font:13px system-ui,sans-serif;color:#fff}
            #m{position:fixed;left:8px;top:8px;padding:6px 12px;background:rgba(0,0,0,.6);border-radius:6px}</style></head>
            <body><div id="m">{{System.Net.WebUtility.HtmlEncode(message)}}</div></body>
            </html>
            """;

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Serialize [value] to a script-safe JS literal — JSON with <c>&lt;/</c> neutralized so no injected
    /// value (a settings string, a widget name) can break out of the surrounding &lt;script&gt; element.</summary>
    private static string JsLiteral(object? value) =>
        JsonSerializer.Serialize(value).Replace("</", "<\\/", StringComparison.Ordinal);

    private static string ReplaceFirst(string haystack, string search, string replacement)
    {
        int index = haystack.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        return index < 0
            ? haystack
            : haystack[..index] + replacement + haystack[(index + search.Length)..];
    }
}
