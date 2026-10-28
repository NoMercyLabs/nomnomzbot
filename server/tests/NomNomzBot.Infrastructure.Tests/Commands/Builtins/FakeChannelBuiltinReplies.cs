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

using NomNomzBot.Application.Commands.Builtin;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// A channel's own per-slot reply texts, held in memory. <see cref="None"/> is a channel that never re-worded a
/// reply; <see cref="Set"/> gives one slot its own text so a test can prove exactly that reply changes.
/// </summary>
internal sealed class FakeChannelBuiltinReplies : IChannelBuiltinReplyOverrides
{
    public static readonly FakeChannelBuiltinReplies None = new();

    private readonly Dictionary<(Guid, string, string), string> _texts = new();

    public FakeChannelBuiltinReplies Set(
        Guid broadcasterId,
        string builtinKey,
        string slot,
        string template
    )
    {
        _texts[(broadcasterId, builtinKey, slot)] = template;
        return this;
    }

    public string? Get(Guid broadcasterId, string builtinKey, string slot) =>
        _texts.GetValueOrDefault((broadcasterId, builtinKey, slot));
}
