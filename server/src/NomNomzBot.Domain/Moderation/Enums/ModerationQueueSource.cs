// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Moderation.Enums;

/// <summary>What enqueued a <c>ModerationQueueItem</c> (moderation.md J.1).</summary>
public enum ModerationQueueSource
{
    AutoMod,
    ViewerReport,
    BotFlag,

    /// <summary>A channel chat filter (hold or flag action) matched the message.</summary>
    ChatFilter,

    /// <summary>A viewer's heat crossed the channel's threshold and no automatic action was taken (S-HEAT-FLAG-HUMAN).</summary>
    HeatThreshold = 10,

    /// <summary>Twitch flagged the chatter as a suspicious user (restricted or monitored) and a message arrived.</summary>
    SuspiciousUser = 11,

    /// <summary>A message from an account or follow younger than the channel's newcomer limit was held.</summary>
    AccountAgeGate = 20,
}
