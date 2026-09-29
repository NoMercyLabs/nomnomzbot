// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
