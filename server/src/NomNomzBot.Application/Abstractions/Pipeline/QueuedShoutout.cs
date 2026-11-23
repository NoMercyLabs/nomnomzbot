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

using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Commands.ValueObjects;

namespace NomNomzBot.Application.Abstractions.Pipeline;

/// <summary>
/// A complete shoutout waiting for Twitch's cooldowns to pass. A shoutout whose channel runs a target carries
/// that target and its variables, so the choice survives the wait and a restart.
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
    DateTimeOffset? NotBefore = null,
    BindingTarget? Response = null,
    IReadOnlyDictionary<string, string>? ResponseVariables = null
);
