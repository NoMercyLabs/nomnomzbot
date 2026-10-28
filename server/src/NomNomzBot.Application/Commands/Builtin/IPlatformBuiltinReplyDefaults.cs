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

namespace NomNomzBot.Application.Commands.Builtin;

/// <summary>
/// The runtime reader of the platform admin's built-in reply texts (plan item A4). The composer asks it for
/// <c>(builtinKey, slot)</c> on every render; the admin editor invalidates it after a save so the next render
/// uses the new text.
/// </summary>
public interface IPlatformBuiltinReplyDefaults
{
    /// <summary>The admin's text for the slot, or null when the slot keeps its shipped wording.</summary>
    Task<string?> GetAsync(string builtinKey, string slot, CancellationToken ct = default);

    /// <summary>Drops the cached texts so the next read loads the saved state.</summary>
    Task InvalidateAsync(CancellationToken ct = default);
}
