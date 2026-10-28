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
