// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
