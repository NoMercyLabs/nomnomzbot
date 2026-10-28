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

using NomNomzBot.Application.Identity.Dtos;

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// Which dedicated Twitch bot account speaks in a channel: the channel's own active custom bot first, else the
/// connected shared platform bot. Null when neither exists, which means the streamer's own account speaks.
/// </summary>
public interface IChannelTwitchBotResolver
{
    Task<ChannelTwitchBot?> ResolveAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );
}
