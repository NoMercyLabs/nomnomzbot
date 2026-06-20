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

// Helix "Polls" category wire models (GET/POST/PATCH /polls). These records deserialize straight from
// Twitch's snake_case JSON via the transport's naming policy — no per-property annotations. Twitch ids
// stay strings; timestamps are DateTimeOffset. The owning tenant is always passed in as a Guid method
// argument and resolved internally — for create/end Twitch also wants the channel id in the request
// body, so those request records carry a resolved BroadcasterId string the sub-client fills in.

/// <summary>One choice in a poll, with its running vote tallies (Get/Create/End Polls).</summary>
public sealed record TwitchPollChoice(
    string Id,
    string Title,
    int Votes,
    int ChannelPointsVotes,
    int BitsVotes
);

/// <summary>A poll the broadcaster created — its choices, voting settings, status and lifecycle timestamps.</summary>
public sealed record TwitchPoll(
    string Id,
    string BroadcasterId,
    string BroadcasterName,
    string BroadcasterLogin,
    string Title,
    IReadOnlyList<TwitchPollChoice> Choices,
    bool BitsVotingEnabled,
    int BitsPerVote,
    bool ChannelPointsVotingEnabled,
    int ChannelPointsPerVote,
    string Status,
    int Duration,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt
);

/// <summary>One choice in a Create Poll request body — only the title is supplied.</summary>
public sealed record CreatePollChoiceRequest(string Title);

/// <summary>
/// Create Poll request body. <see cref="BroadcasterId"/> is the resolved Twitch channel id (Twitch wants
/// it in the body for this endpoint, not the query); the sub-client fills it from the tenant Guid. Channel
/// points voting is opt-in — leave the points fields null to disable it (the transport omits nulls).
/// </summary>
public sealed record CreatePollRequest(
    string Title,
    IReadOnlyList<CreatePollChoiceRequest> Choices,
    int Duration,
    bool? ChannelPointsVotingEnabled = null,
    int? ChannelPointsPerVote = null,
    string BroadcasterId = ""
);

/// <summary>
/// End Poll request body. <see cref="BroadcasterId"/> is the resolved Twitch channel id (Twitch wants it in
/// the body for this endpoint); the sub-client fills it from the tenant Guid. <see cref="Status"/> is
/// <c>TERMINATED</c> (end now, keep visible) or <c>ARCHIVED</c> (end and hide).
/// </summary>
public sealed record EndPollRequest(string Id, string Status, string BroadcasterId = "");
