// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

// Helix "Ads" category wire models (POST /channels/commercial, GET /channels/ads,
// POST /channels/ads/schedule/snooze). These records deserialize straight from Twitch's snake_case JSON
// via the transport's naming policy — no per-property annotations. The ad-schedule timestamps arrive as
// unix seconds (0 when there is no scheduled/last ad), so they stay int rather than DateTimeOffset. The
// owning tenant is always passed in as a Guid method argument and resolved internally — Start Commercial
// wants the channel id in the request body, so that request record carries a resolved BroadcasterId string
// the sub-client fills in.

/// <summary>Result of starting a commercial — the length served, a status message, and the retry cooldown.</summary>
public sealed record TwitchCommercial(int Length, string Message, int RetryAfter);

/// <summary>
/// The channel's ad schedule — next/last ad as unix seconds (0 when none), the current ad duration, the
/// remaining pre-roll-free time, and the snooze allowance (count plus the unix refresh time).
/// </summary>
public sealed record TwitchAdSchedule(
    int SnoozeCount,
    int SnoozeRefreshAt,
    int NextAdAt,
    int Duration,
    int LastAdAt,
    int PrerollFreeTime
);

/// <summary>Result of snoozing the next ad — the updated snooze count, refresh time, and pushed-back next-ad time (unix seconds).</summary>
public sealed record TwitchAdSnooze(int SnoozeCount, int SnoozeRefreshAt, int NextAdAt);

/// <summary>
/// Start Commercial request body. <see cref="BroadcasterId"/> is the resolved Twitch channel id (Twitch wants
/// it in the body for this endpoint, not the query); the sub-client fills it from the tenant Guid.
/// <see cref="Length"/> is the desired commercial length in seconds (Twitch may serve a shorter one).
/// </summary>
public sealed record StartCommercialRequest(int Length, string BroadcasterId = "");
