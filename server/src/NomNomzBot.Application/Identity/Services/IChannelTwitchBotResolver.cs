// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
