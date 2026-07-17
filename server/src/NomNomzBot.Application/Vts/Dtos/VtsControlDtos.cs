// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;

namespace NomNomzBot.Application.Vts.Dtos;

/// <summary>The outcome of one VTS API request: OBS-style ok/data/error (vtube-studio.md §3).</summary>
public sealed record VtsRequestResult(bool Ok, string? DataJson, string? Error);

/// <summary>A model move (vtube-studio.md §3 — maps to <c>MoveModelRequest</c>).</summary>
public sealed record VtsMove(
    double? X,
    double? Y,
    double? Rotation,
    double? Size,
    double TimeSeconds = 0.3,
    bool Relative = false
);

/// <summary>A model color tint (maps to <c>ColorTintRequest</c>); null tag = tint everything.</summary>
public sealed record VtsColorTint(
    byte R,
    byte G,
    byte B,
    byte A = 255,
    string? MatchArtMeshTag = null
);

/// <summary>One available VTS model.</summary>
public sealed record VtsModelRef(string Id, string Name, bool IsLoaded);

/// <summary>One hotkey in the current model.</summary>
public sealed record VtsHotkeyRef(string Id, string Name, string Type);

/// <summary>Everything the editor pickers need: models + current-model hotkeys + expressions.</summary>
public sealed record VtsModelInventory(
    IReadOnlyList<VtsModelRef> Models,
    IReadOnlyList<VtsHotkeyRef> Hotkeys,
    IReadOnlyList<string> Expressions
);

/// <summary>REST body for the raw control route.</summary>
public sealed record VtsControlRequest
{
    [Required]
    [MaxLength(100)]
    public string RequestType { get; init; } = null!;

    /// <summary>The VTS request <c>data</c> object as JSON (null = empty).</summary>
    public string? PayloadJson { get; init; }
}
