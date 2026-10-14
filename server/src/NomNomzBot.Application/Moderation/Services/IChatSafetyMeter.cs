// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Moderation.Dtos;

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// The per-channel chat safety meter, in memory. Reads only: the status reports and never acts.
/// </summary>
public interface IChatSafetyMeter
{
    /// <summary>The channel's level and meters as of now.</summary>
    ChatSafetyStatusDto Snapshot(Guid channelId);

    /// <summary>Every channel the meter has seen a signal for.</summary>
    IReadOnlyList<Guid> ActiveChannels();
}
