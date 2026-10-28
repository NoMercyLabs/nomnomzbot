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
