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

namespace NomNomzBot.Application.Community.Dtos;

/// <summary>One choice (poll) or outcome (prediction) of a past poll or prediction.</summary>
/// <param name="Id">The Twitch choice or outcome id.</param>
/// <param name="Title">The label viewers saw.</param>
/// <param name="Votes">Poll: total votes (normal plus channel points). Prediction: number of users who picked it.</param>
/// <param name="ChannelPoints">Poll: votes cast with channel points. Prediction: points put on it.</param>
/// <param name="SharePercent">This option's share of all votes, 0 to 100, rounded to a whole number.</param>
/// <param name="IsWinner">True for the winning choice or outcome.</param>
public sealed record PollPredictionOptionDto(
    string Id,
    string Title,
    int Votes,
    int ChannelPoints,
    int SharePercent,
    bool IsWinner
);

/// <summary>
/// One poll or prediction the channel ran, with its final result, folded from the event journal.
/// <paramref name="Kind"/> is <c>poll</c> or <c>prediction</c>. <paramref name="Status"/> is the Twitch end status
/// (completed, archived, terminated, resolved, canceled) or <c>unfinished</c> when the journal holds a start but no end.
/// </summary>
public sealed record PollPredictionHistoryItemDto(
    string Id,
    string Kind,
    string Title,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int TotalVotes,
    IReadOnlyList<PollPredictionOptionDto> Options
);
