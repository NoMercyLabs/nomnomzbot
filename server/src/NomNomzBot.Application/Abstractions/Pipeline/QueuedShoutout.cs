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
/// A complete shoutout waiting for Twitch's cooldowns to pass.
/// </summary>
public sealed record QueuedShoutout(
    Guid BroadcasterId,
    TwitchUser Target,
    string TriggeredByUserId,
    bool IsRaid,
    TimeSpan GlobalCooldown,
    TimeSpan PerUserCooldown,
    DateTimeOffset EnqueuedAt,
    string Announcement = "",
    bool Speak = false,
    Guid? ActorUserId = null,
    DateTimeOffset? NotBefore = null
);
