// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
