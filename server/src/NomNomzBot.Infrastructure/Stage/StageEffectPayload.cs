// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
