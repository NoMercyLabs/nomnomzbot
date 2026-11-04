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

using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Arguments;

/// <summary>A command argument that is a duration for the duration matrix cell.</summary>
internal static class ArgsSurfaceFixture
{
    public static IEnumerable<string> ShapesFor(string key) => key == "args.duration" ? [":1"] : [];

    public static void Seed(
        TemplateHelperContext surface,
        string key,
        Dictionary<string, string> seeds
    )
    {
        if (
            key == "args.duration"
            && surface is TemplateHelperContext.Command or TemplateHelperContext.Pipeline
        )
            seeds["args"] = "5m samplefriend";
    }
}
