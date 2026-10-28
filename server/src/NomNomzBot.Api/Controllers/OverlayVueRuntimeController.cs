// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace NomNomzBot.Api.Controllers;

/// <summary>
/// Serves the Vue runtime (<c>/overlay/vue.js</c>) — the vendored <c>vue@3.5.39</c> global build that exposes
/// <c>window.Vue</c> (<c>createApp</c> + the compiled-render-fn helpers). A compiled vue widget bundle keeps its
/// <c>vue</c> imports external and self-mounts via <c>window.Vue.createApp(...)</c>, so the overlay host injects
/// this script into the widget's sandboxed iframe BEFORE the bundle runs (see <c>OverlayHostController</c>). The
/// runtime version is pinned to the <c>@vue/compiler-sfc</c> the server compiles with. Served anonymously; carries
/// no secrets, and is long-cacheable (it only changes with a bot upgrade).
/// </summary>
[ApiController]
[Route("overlay")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[EnableRateLimiting(NomNomzBot.Api.RateLimiting.RateLimitPolicyNames.Anonymous)]
public sealed class OverlayVueRuntimeController : ControllerBase
{
    // The runtime is ~104 kb; read it out of the Infrastructure assembly once and cache the string for every
    // request (the embedded name is pinned by an explicit LogicalName in NomNomzBot.Infrastructure.csproj).
    private const string ResourceName =
        "NomNomzBot.Infrastructure.Widgets.Vendor.vue.runtime.global.prod.js";

    private static readonly string Runtime = LoadRuntime();

    private static string LoadRuntime()
    {
        Assembly assembly = typeof(Infrastructure.DependencyInjection).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
            throw new InvalidOperationException(
                $"Embedded Vue runtime '{ResourceName}' was not found in {assembly.GetName().Name}."
            );
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The Vue runtime script. Long-cacheable + immutable — the content only changes with a bot upgrade.</summary>
    [HttpGet("vue.js")]
    public IActionResult Get()
    {
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return Content(Runtime, "application/javascript; charset=utf-8");
    }
}
