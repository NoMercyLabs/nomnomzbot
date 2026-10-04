// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
