// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Infrastructure.Identity;

public sealed class AdminService : IAdminService
{
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly HealthCheckService _healthChecks;
    private readonly IPlatformBotReadinessGate _botReadiness;

    public AdminService(
        IApplicationDbContext db,
        TimeProvider timeProvider,
        HealthCheckService healthChecks,
        IPlatformBotReadinessGate botReadiness
    )
    {
        _db = db;
        _timeProvider = timeProvider;
        _healthChecks = healthChecks;
        _botReadiness = botReadiness;
    }

    public async Task<Result<AdminStatsDto>> GetStatsAsync(CancellationToken ct = default)
    {
        int totalChannels = await _db.Channels.CountAsync(ct);
        int activeChannels = await _db.Channels.CountAsync(c => c.IsLive, ct);
        int totalUsers = await _db.Users.CountAsync(ct);

        DateTime today = _timeProvider.GetUtcNow().UtcDateTime.Date;
        int eventsToday = await _db.ChannelEvents.CountAsync(e => e.CreatedAt >= today, ct);

        Process process = Process.GetCurrentProcess();
        long uptimeSeconds = (long)
            (
                _timeProvider.GetUtcNow().UtcDateTime - process.StartTime.ToUniversalTime()
            ).TotalSeconds;

        AdminStatsDto dto = new(
            totalChannels,
            activeChannels,
            totalUsers,
            "healthy",
            uptimeSeconds,
            eventsToday
        );

        return Result.Success(dto);
    }

    public async Task<Result<PagedList<AdminChannelDto>>> ListChannelsAsync(
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        int total = await _db.Channels.CountAsync(ct);

        List<AdminChannelDto> items = await (
            from c in _db.Channels
            join sub in _db.ChannelSubscriptions on c.Id equals sub.BroadcasterId into subs
            from sub in subs.OrderByDescending(s => s.CreatedAt).Take(1).DefaultIfEmpty()
            orderby c.CreatedAt descending
            select new AdminChannelDto(
                c.Id.ToString(),
                c.User.DisplayName,
                c.Name,
                c.IsLive,
                c.Enabled,
                0,
                sub != null ? sub.Tier : "free",
                c.CreatedAt
            )
        )
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        return Result.Success(
            new PagedList<AdminChannelDto>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<PagedList<AdminUserDto>>> ListUsersAsync(
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        int total = await _db.Users.CountAsync(ct);

        List<AdminUserDto> items = await _db
            .Users.OrderByDescending(u => u.CreatedAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .Select(u => new AdminUserDto(
                u.Id.ToString(),
                u.DisplayName,
                u.Username,
                null,
                u.IsPlatformPrincipal ? "admin" : "user",
                _db.Channels.Count(c => c.OwnerUserId == u.Id),
                u.CreatedAt,
                u.UpdatedAt
            ))
            .ToListAsync(ct);

        return Result.Success(
            new PagedList<AdminUserDto>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<AdminSystemDto>> GetSystemHealthAsync(CancellationToken ct = default)
    {
        Process process = Process.GetCurrentProcess();
        long memoryMb = process.WorkingSet64 / (1024 * 1024);

        string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";

        // REAL probes — the same registered health checks the public /health endpoint runs (per profile:
        // postgres+redis on the durable tier, the lite checks on SQLite), never a canned "healthy" list.
        HealthReport report = await _healthChecks.CheckHealthAsync(ct);
        List<ServiceHealthDto> services =
        [
            new("api", "healthy", null), // this code answering IS the probe
            .. report.Entries.Select(e => new ServiceHealthDto(
                e.Key,
                ToStatus(e.Value.Status),
                (int?)e.Value.Duration.TotalMilliseconds
            )),
        ];

        // The bot is healthy when its token actually resolves and decrypts — the signal a bot-scoped
        // Twitch call would succeed (false on a fresh install or after a KEK rotation pending re-auth).
        bool botReady = await _botReadiness.IsPlatformBotConfiguredAsync(ct);
        services.Add(new("bot", botReady ? "healthy" : "degraded", null));

        string overall =
            services.Any(s => s.Status == "unhealthy") ? "unhealthy"
            : services.Any(s => s.Status == "degraded") ? "degraded"
            : "healthy";

        AdminSystemDto dto = new(overall, services, version, memoryMb, 0);
        return Result.Success(dto);
    }

    private static string ToStatus(HealthStatus status) =>
        status switch
        {
            HealthStatus.Healthy => "healthy",
            HealthStatus.Degraded => "degraded",
            _ => "unhealthy",
        };
}
