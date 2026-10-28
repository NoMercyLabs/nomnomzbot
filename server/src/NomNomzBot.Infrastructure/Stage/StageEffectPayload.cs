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

namespace NomNomzBot.Infrastructure.Stage;

/// <summary>
/// <c>stage_effect</c>: one effect for the stage widget to play. <c>Text</c> is plain text the widget sets through
/// a text node, never markup. <c>Intensity</c> and <c>Color</c> are null when the caller left them out, and the
/// widget then uses its own channel settings.
/// </summary>
public sealed record StageEffectPayload(
    string Preset,
    string? Text,
    int DurationMs,
    int? Intensity,
    string? Color
);
