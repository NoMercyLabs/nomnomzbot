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
/// The runtime reader of a channel's own built-in reply texts (commands-pipelines.md §11) — the top rung of the
/// composer's precedence ladder. The composer asks it for <c>(broadcasterId, builtinKey, slot)</c> on every
/// render, so every reply of every built-in is overridable without the built-in threading anything through.
/// </summary>
public interface IChannelBuiltinReplyOverrides
{
    /// <summary>The channel's text for exactly this slot, or null when the slot keeps its default.</summary>
    string? Get(Guid broadcasterId, string builtinKey, string slot);
}
