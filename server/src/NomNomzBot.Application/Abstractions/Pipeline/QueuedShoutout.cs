// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Application.Abstractions.Pipeline;

/// <summary>
/// The native Twitch /shoutout of a manual or raid shoutout, waiting for Twitch's global cooldown to pass.
/// Its chat announcement and TTS have already gone out.
/// </summary>
/// <param name="Attempts">How many times a send of this item has already failed.</param>
public sealed record QueuedShoutout(
    Guid BroadcasterId,
    TwitchUser Target,
    string TriggeredByUserId,
    bool IsRaid,
    TimeSpan GlobalCooldown,
    TimeSpan PerUserCooldown,
    DateTimeOffset EnqueuedAt,
    int Attempts = 0
);
