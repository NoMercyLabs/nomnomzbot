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
