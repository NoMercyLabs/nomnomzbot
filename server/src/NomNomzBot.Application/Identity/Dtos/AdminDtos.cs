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
