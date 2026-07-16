// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;

namespace NomNomzBot.Application.Community.Dtos;

/// <summary>One option's live tally.</summary>
public sealed record ChatPollOptionDto(int Index, string Label, int Votes);

/// <summary>
/// A bot-run chat poll with LIVE per-option tallies (vote by typing the option number in chat — works
/// on every platform, no affiliate gate). Status: open | closed.
/// </summary>
public sealed record ChatPollDto(
    Guid Id,
    string Question,
    IReadOnlyList<ChatPollOptionDto> Options,
    string Status,
    int TotalVotes,
    DateTime OpenedAt,
    DateTime? ClosesAt,
    DateTime? ClosedAt
);

public sealed record OpenChatPollRequest
{
    [Required]
    [MaxLength(200)]
    public required string Question { get; init; }

    /// <summary>2–10 option labels, voted by 1-based index.</summary>
    [Required]
    [MinLength(2)]
    [MaxLength(10)]
    public required List<string> Options { get; init; }

    /// <summary>Optional auto-close horizon in seconds (max 24h); null = open until closed manually.</summary>
    public int? DurationSeconds { get; init; }

    /// <summary>Announce the poll (question + numbered options + how to vote) in chat on open. Default true.</summary>
    public bool Announce { get; init; } = true;
}
