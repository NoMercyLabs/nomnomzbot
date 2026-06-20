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

// Helix "Schedule" category wire models (GET /schedule, POST/PATCH/DELETE /schedule/segment,
// PATCH /schedule/settings). These records deserialize straight from Twitch's snake_case JSON via the
// transport's naming policy — no per-property annotations. The schedule is a single nested object
// (not a data[] array of rows): one schedule that carries its segments, its vacation window and the
// owning broadcaster's identity. Twitch ids stay strings; the owning tenant is always passed in as a
// Guid method argument, never here.

/// <summary>
/// Get Channel Stream Schedule — the broadcaster's streaming schedule: the segment list, the active
/// vacation window (null when none), and the broadcaster's identity. The data is a single object, so the
/// transport's <c>data[]</c> envelope wraps exactly one of these.
/// </summary>
public sealed record TwitchSchedule(
    IReadOnlyList<TwitchScheduleSegment> Segments,
    string BroadcasterId,
    string BroadcasterName,
    string BroadcasterLogin,
    TwitchScheduleVacation? Vacation
);

/// <summary>One broadcast segment in the schedule (a single or recurring entry).</summary>
public sealed record TwitchScheduleSegment(
    string Id,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    string Title,
    DateTimeOffset? CanceledUntil,
    TwitchScheduleCategory? Category,
    bool IsRecurring
);

/// <summary>The category (game) a schedule segment is tagged with.</summary>
public sealed record TwitchScheduleCategory(string Id, string Name);

/// <summary>The broadcaster's scheduled vacation window during which the schedule is suspended.</summary>
public sealed record TwitchScheduleVacation(DateTimeOffset StartTime, DateTimeOffset EndTime);

/// <summary>
/// Create Channel Stream Schedule Segment request body. <c>StartTime</c> is the RFC3339 segment start,
/// <c>Timezone</c> is the IANA zone the broadcast airs in, and <c>Duration</c> is the length in minutes
/// (a string per Twitch). The remaining fields are optional; the transport omits nulls. The broadcaster
/// is the Guid method argument, not part of this body.
/// </summary>
public sealed record CreateScheduleSegmentRequest(
    DateTimeOffset StartTime,
    string Timezone,
    string Duration,
    bool? IsRecurring = null,
    string? CategoryId = null,
    string? Title = null
);

/// <summary>
/// Update Channel Stream Schedule Segment request body. All fields optional — only the ones set are sent
/// (the transport omits nulls), matching Twitch's "patch only what you provide" semantics. The broadcaster
/// and the segment id are method arguments, not part of this body.
/// </summary>
public sealed record UpdateScheduleSegmentRequest(
    DateTimeOffset? StartTime = null,
    string? Duration = null,
    string? CategoryId = null,
    string? Title = null,
    bool? IsCanceled = null,
    string? Timezone = null
);
