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
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// See <see cref="IChannelBuiltinReplyOverrides"/>. Reads the channel registry's in-memory cache, which the
/// registry loads from <c>ChannelBuiltinCommand.OverridesJson</c> and reloads on every write
/// (<see cref="IChannelRegistry.InvalidateBuiltinsAsync"/>) — so a reply renders with no database hit. A channel
/// the registry has not loaded has no chat activity to reply to, so it reads as "no override".
/// </summary>
public sealed class ChannelBuiltinReplyOverridesReader(IChannelRegistry registry)
    : IChannelBuiltinReplyOverrides
{
    public string? Get(Guid broadcasterId, string builtinKey, string slot) =>
        registry
            .Get(broadcasterId)
            ?.BuiltinReplyOverrides.GetValueOrDefault(
                ChannelContext.BuiltinReplyKey(builtinKey, slot)
            );
}
