// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Identity.Dtos;

public sealed record AdminStatsDto(
    int TotalChannels,
    int ActiveChannels,
    int TotalUsers,
    string SystemStatus,
    long BotUptimeSeconds,
    int EventsProcessedToday
);

public sealed record AdminChannelDto(
    string Id,
    string DisplayName,
    string Login,
    bool IsLive,
    bool IsActive,
    int ViewerCount,
    string Plan,
    DateTime CreatedAt
);

public sealed record ServiceHealthDto(string Name, string Status, int? LatencyMs);

public sealed record AdminUserDto(
    string Id,
    string DisplayName,
    string Login,
    string? Email,
    string Role,
    int ChannelCount,
    DateTime CreatedAt,
    DateTime? LastActive
);

public sealed record AdminSystemDto(
    string Overall,
    List<ServiceHealthDto> Services,
    string BotVersion,
    long MemoryUsageMb,
    double CpuPercent
);

// ── EventSub subscription health (S-ADMIN-6a) ──

/// <summary>
/// One topic in the real EventSub registry <c>TwitchEventSubHostedService</c> maintains — never a fabricated
/// row. <see cref="LastConfirmedAt"/> is the registry row's own <c>UpdatedAt</c>, the last time this exact
/// status was persisted (created, re-homed on reconnect, or flipped by a Twitch revocation notice).
/// </summary>
public sealed record AdminEventSubTopicHealthDto(
    Guid SubscriptionId,
    string EventType,
    string Version,
    string Status,
    bool Enabled,
    string? LastError,
    DateTime LastConfirmedAt
);

/// <summary>One tenant's EventSub registry rows, grouped for the 2am operator console (S-ADMIN-6a).</summary>
public sealed record AdminEventSubTenantHealthDto(
    Guid BroadcasterId,
    string ChannelDisplayName,
    IReadOnlyList<AdminEventSubTopicHealthDto> Topics
);

// ── Outbound webhook delivery log + replay (S-ADMIN-6a) ──

/// <summary>One delivery attempt across ALL tenants, for the platform-wide delivery log. <see cref="EndpointName"/>
/// reads "(deleted endpoint)" when the endpoint has since been soft-deleted — the row is kept, never hidden.</summary>
public sealed record AdminWebhookDeliveryDto(
    long Id,
    Guid BroadcasterId,
    Guid EndpointId,
    string EndpointName,
    bool EndpointCanReplay,
    string EventType,
    int Attempt,
    string Status,
    int? ResponseCode,
    int? DurationMs,
    string? Error,
    DateTime CreatedAt
);

/// <summary>
/// The outcome of an admin-initiated replay: a genuinely NEW delivery row was created and sent — the original
/// attempt this replays is never mutated.
/// </summary>
public sealed record AdminWebhookReplayResultDto(
    long OriginalDeliveryId,
    long NewDeliveryId,
    string Status,
    int? ResponseCode
);
