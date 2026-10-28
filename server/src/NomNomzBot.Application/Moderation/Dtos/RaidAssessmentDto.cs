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

namespace NomNomzBot.Application.Moderation.Dtos;

/// <summary>One assessed incoming raid.</summary>
public sealed record RaidAssessmentDto(
    Guid Id,
    string RaiderTwitchUserId,
    string RaiderLogin,
    string RaiderDisplayName,
    int ViewerCount,
    DateTime? AccountCreatedAt,
    int EarlierRaidCount,
    bool IsBanned,
    bool IsNetworkBlocked,
    string Verdict,
    DateTime OccurredAt
);
