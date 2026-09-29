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
/// The runtime reader of a channel's own built-in reply texts (commands-pipelines.md §11) — the top rung of the
/// composer's precedence ladder. The composer asks it for <c>(broadcasterId, builtinKey, slot)</c> on every
/// render, so every reply of every built-in is overridable without the built-in threading anything through.
/// </summary>
public interface IChannelBuiltinReplyOverrides
{
    /// <summary>The channel's text for exactly this slot, or null when the slot keeps its default.</summary>
    string? Get(Guid broadcasterId, string builtinKey, string slot);
}
