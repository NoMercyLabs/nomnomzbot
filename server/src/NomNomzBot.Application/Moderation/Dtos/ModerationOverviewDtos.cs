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

/// <summary>Whether the channel's Twitch Shield Mode is active.</summary>
public sealed record ShieldModeDto(bool Enabled);

/// <summary>Today's moderation counts for a channel, derived from its event log.</summary>
public sealed record ModerationStatsDto(
    int BansToday,
    int Timeouts,
    int DeletedMessages,
    int AutomodActions
);
