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

/// <summary>A manual or raid shoutout that waits for Twitch's global shoutout cooldown to pass.</summary>
/// <param name="Attempts">How many times a send of this item has already failed.</param>
public sealed record QueuedShoutout(
    Guid BroadcasterId,
    TwitchUser Target,
    string Announcement,
    bool Speak,
    string TriggeredByUserId,
    bool IsRaid,
    TimeSpan GlobalCooldown,
    TimeSpan PerUserCooldown,
    DateTimeOffset EnqueuedAt,
    int Attempts = 0
);
