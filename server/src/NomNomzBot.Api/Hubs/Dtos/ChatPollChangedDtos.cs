// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Api.Hubs.Dtos;

/// <summary>A bot-run chat poll opened, got a vote, or closed. Carries the whole poll so the card needs no refetch.</summary>
public record ChatPollChangedAlertDto(
    Guid PollId,
    string Change,
    string Question,
    string Status,
    int TotalVotes,
    IReadOnlyList<ChatPollChangedOptionDto> Options,
    DateTime OpenedAt,
    DateTime? ClosesAt,
    DateTime? ClosedAt
);

/// <summary>One option of a changed chat poll, with its current vote count.</summary>
public record ChatPollChangedOptionDto(int Index, string Label, int Votes);
