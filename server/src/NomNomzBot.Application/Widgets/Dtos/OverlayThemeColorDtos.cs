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

namespace NomNomzBot.Application.Widgets.Dtos;

/// <summary>The channel's theme color for every overlay: a <c>#RRGGBB</c> value, or empty for "each theme's own color".</summary>
public sealed record OverlayThemeColor(string Color);

/// <summary>A new theme color (<c>#RRGGBB</c>), or an empty one to clear it.</summary>
public sealed record UpdateOverlayThemeColorRequest(string Color);
