// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

// Helix "Hype Train" category wire models (GET /hypetrain/status). These records deserialize straight
// from Twitch's snake_case JSON via the transport's naming policy — no per-property annotations. Twitch
// ids stay strings (they are users' / broadcasters' ids); the owning tenant is always passed in as a Guid
// method argument, never here. The status envelope holds a single object whose nested members are null
// when the channel has no active train / no recorded history (Get Hype Train Status).

/// <summary>
/// Get Hype Train Status — one channel's Hype Train state. <see cref="Current"/> is null when no train is
/// active; <see cref="AllTimeHigh"/> / <see cref="SharedAllTimeHigh"/> are null until a (shared) train has
/// occurred. An empty <c>data</c> envelope surfaces as <c>not_found</c> upstream (no Hype Train activity).
/// </summary>
public sealed record TwitchHypeTrainStatus(
    TwitchHypeTrain? Current,
    TwitchHypeTrainRecord? AllTimeHigh,
    TwitchHypeTrainRecord? SharedAllTimeHigh
);

/// <summary>The currently active Hype Train: its level, point totals, progress, contributors and timing.</summary>
public sealed record TwitchHypeTrain(
    string Id,
    string BroadcasterUserId,
    string BroadcasterUserLogin,
    string BroadcasterUserName,
    int Level,
    int Total,
    int Progress,
    int Goal,
    IReadOnlyList<TwitchHypeTrainContribution> TopContributions,
    IReadOnlyList<TwitchHypeTrainParticipant>? SharedTrainParticipants,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    string Type,
    bool IsSharedTrain
);

/// <summary>One top contributor to the active Hype Train. <see cref="Type"/> is <c>bits</c>, <c>subscription</c>, or <c>other</c>.</summary>
public sealed record TwitchHypeTrainContribution(
    string UserId,
    string UserLogin,
    string UserName,
    string Type,
    int Total
);

/// <summary>One broadcaster participating in a shared Hype Train (Get Hype Train Status).</summary>
public sealed record TwitchHypeTrainParticipant(
    string BroadcasterUserId,
    string BroadcasterUserLogin,
    string BroadcasterUserName
);

/// <summary>A channel's record Hype Train: the highest level / total it has reached and when (all-time or shared all-time).</summary>
public sealed record TwitchHypeTrainRecord(int Level, int Total, DateTimeOffset AchievedAt);
