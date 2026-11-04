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

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Chat;

/// <summary>A pipeline Discord post carries the raw chat message from its trigger.</summary>
internal static class RawSurfaceFixture
{
    public static void Seed(
        TemplateHelperContext surface,
        string key,
        Dictionary<string, string> seeds
    )
    {
        if (surface == TemplateHelperContext.Discord && key == "raw.message")
            seeds["raw.message"] = "sampleHype";
    }
}
