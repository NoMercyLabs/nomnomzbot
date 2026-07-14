// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
public sealed class OverlayVueRuntimeController : ControllerBase
{
    // The runtime is ~104 kb; read it out of the Infrastructure assembly once and cache the string for every
    // request (the embedded name is pinned by an explicit LogicalName in NomNomzBot.Infrastructure.csproj).
    private const string ResourceName =
        "NomNomzBot.Infrastructure.Widgets.Vendor.vue.runtime.global.prod.js";

    private static readonly string Runtime = LoadRuntime();

    private static string LoadRuntime()
    {
        Assembly assembly = typeof(NomNomzBot.Infrastructure.DependencyInjection).Assembly;
        using System.IO.Stream? stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
            throw new InvalidOperationException(
                $"Embedded Vue runtime '{ResourceName}' was not found in {assembly.GetName().Name}."
            );
        using System.IO.StreamReader reader = new(stream);
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
