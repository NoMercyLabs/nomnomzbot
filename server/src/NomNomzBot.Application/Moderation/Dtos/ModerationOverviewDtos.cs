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

/// <summary>Whether the channel's Twitch Shield Mode is active.</summary>
public sealed record ShieldModeDto(bool Enabled);

/// <summary>Today's moderation counts for a channel, derived from its event log.</summary>
public sealed record ModerationStatsDto(
    int BansToday,
    int Timeouts,
    int DeletedMessages,
    int AutomodActions
);
