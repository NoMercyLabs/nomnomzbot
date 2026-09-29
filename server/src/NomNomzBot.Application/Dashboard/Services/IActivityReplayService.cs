// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Dashboard.Dtos;

namespace NomNomzBot.Application.Dashboard.Services;

/// <summary>
/// Replays one past activity-feed event the way viewers experienced it: the bot's chat reply, the TTS and the
/// overlay alerts. A gift bomb replays as its whole chain — the gifter's line, then each recipient's line, in
/// the original order. Presentation only: it never grants currency or loyalty, never fulfils a reward, never
/// touches counters, stats or hype-train state, and never writes an activity or journal row.
/// </summary>
public interface IActivityReplayService
{
    /// <summary>
    /// Fails with <c>NOT_FOUND</c> when the event can neither be rebuilt from the event journal nor has a
    /// captured overlay alert — there is nothing to replay.
    /// </summary>
    Task<Result<ActivityReplayResult>> ReplayAsync(
        Guid broadcasterId,
        string channelEventId,
        CancellationToken ct = default
    );
}
