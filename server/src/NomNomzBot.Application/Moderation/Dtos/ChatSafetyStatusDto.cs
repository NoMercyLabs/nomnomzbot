// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Moderation.ChatSafety;

namespace NomNomzBot.Application.Moderation.Dtos;

/// <summary>
/// How safe a channel's chat is right now: a level and four live meters over the last 60 seconds.
/// </summary>
/// <param name="Level">The level the channel reads at.</param>
/// <param name="MessagesPerSecond">Chat lines in the last 60 seconds, divided by 60.</param>
/// <param name="FollowsPerMinute">Follows in the last 60 seconds.</param>
/// <param name="NewAccountsPerMinute">Accounts younger than 7 days that wrote their first line of the stream in the last 60 seconds.</param>
/// <param name="FlaggedLinesPerMinute">Lines spam defense acted on (not dry run) or AutoMod held, in the last 60 seconds.</param>
public sealed record ChatSafetyStatusDto(
    ChatSafetyLevel Level,
    double MessagesPerSecond,
    int FollowsPerMinute,
    int NewAccountsPerMinute,
    int FlaggedLinesPerMinute
);
