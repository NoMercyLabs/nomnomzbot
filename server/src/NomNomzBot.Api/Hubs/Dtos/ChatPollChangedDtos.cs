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
