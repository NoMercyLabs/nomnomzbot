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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;

namespace NomNomzBot.Application.Widgets.Services;

/// <summary>
/// The channel's one theme color for every overlay, kept as the per-channel configuration key
/// <see cref="ConfigKey"/>. A widget's own color wins over it; it wins over the theme's own color.
/// </summary>
public interface IOverlayThemeColorService
{
    /// <summary>The per-channel configuration key that holds the color (a hex value, or empty).</summary>
    public const string ConfigKey = "overlay.themeColor";

    /// <summary>The setting every widget receives when the channel has a color set.</summary>
    public const string SettingKey = "globalAccentColor";

    /// <summary>The channel's color; a channel with none gets an empty color and nothing is written.</summary>
    Task<Result<OverlayThemeColor>> GetAsync(Guid broadcasterId, CancellationToken ct = default);

    /// <summary>
    /// Stores the color, or clears it when the request color is empty. Anything that is not <c>#RRGGBB</c> is
    /// refused with the localized key <c>overlay_theme_color_invalid</c>.
    /// </summary>
    Task<Result<OverlayThemeColor>> SetAsync(
        Guid broadcasterId,
        UpdateOverlayThemeColorRequest request,
        CancellationToken ct = default
    );
}
