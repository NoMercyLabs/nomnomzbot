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

namespace NomNomzBot.Application.Widgets.Services;

/// <summary>
/// The one place that decides which widget frameworks have build AND runtime support. React and svelte have neither
/// (no overlay runtime is injected for them), so every entry point — build, create, save, gallery submit — refuses
/// them with the same code instead of accepting a widget that can never render.
/// </summary>
public static class WidgetFrameworkSupport
{
    /// <summary>The error code every refusal carries; the API maps it to a 400.</summary>
    public const string UnsupportedCode = "WIDGET_FRAMEWORK_UNSUPPORTED";

    public static bool IsUnsupported(string? framework)
    {
        string normalized = (framework ?? string.Empty).Trim();
        return normalized.Equals("react", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("svelte", StringComparison.OrdinalIgnoreCase);
    }

    public static string UnsupportedMessage(string? framework) =>
        $"Framework '{(framework ?? string.Empty).Trim()}' has no build and runtime support yet. Use 'vanilla' or 'vue'.";
}
