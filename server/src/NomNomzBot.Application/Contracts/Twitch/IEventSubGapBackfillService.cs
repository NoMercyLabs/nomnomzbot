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

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// Backfills the events a WebSocket EventSub gap silently dropped (twitch-eventsub.md: EventSub itself never
/// redelivers anything after a disconnect — it just resumes live). Twitch has no generic "everything that
/// happened" API, so this only covers the event types Twitch DOES let a client list with a timestamp: channel
/// point redemptions and follows. Each backfilled event is published through the SAME <c>IEventBus</c> path a
/// live translator would use, with a DETERMINISTIC <c>EventId</c> derived from the event's own stable Twitch
/// identity (redemption id; user id + follow timestamp) — so re-running a backfill for an overlapping or
/// identical window (e.g. two reconnects in quick succession) never re-publishes, and never double-fires the
/// side effects (chat announcements, pipeline triggers, currency/economy awards) a duplicate publish would
/// cause. A candidate already present in the journal (by that deterministic id) is skipped before publish,
/// not merely deduped after the fact.
/// </summary>
public interface IEventSubGapBackfillService
{
    /// <summary>
    /// Sweeps <paramref name="broadcasterId"/>'s manageable custom-reward redemptions and its follower list for
    /// items whose Twitch timestamp falls in <c>[gapStart, gapEnd]</c>, filters out anything already journaled,
    /// and publishes the rest in chronological order. Returns the count of NEWLY published events (0 is a
    /// legitimate success — nothing was missed). A read failure against Twitch (e.g. missing scope, rate
    /// limit) is reported per source and does not abort the other source's sweep.
    /// </summary>
    Task<Result<int>> BackfillGapAsync(
        Guid broadcasterId,
        DateTimeOffset gapStart,
        DateTimeOffset gapEnd,
        CancellationToken ct = default
    );
}
