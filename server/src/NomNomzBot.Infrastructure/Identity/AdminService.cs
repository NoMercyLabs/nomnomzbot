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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Infrastructure.Identity;

public sealed class AdminService : IAdminService
{
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public AdminService(IApplicationDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
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

    public Task<Result<AdminSystemDto>> GetSystemHealthAsync(CancellationToken ct = default)
    {
        Process process = Process.GetCurrentProcess();
        long memoryMb = process.WorkingSet64 / (1024 * 1024);
        long uptimeSeconds = (long)
            (
                _timeProvider.GetUtcNow().UtcDateTime - process.StartTime.ToUniversalTime()
            ).TotalSeconds;

        string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";

        List<ServiceHealthDto> services =
        [
            new("api", "healthy", null),
            new("database", "healthy", null),
            new("bot", uptimeSeconds > 0 ? "healthy" : "degraded", null),
        ];

        AdminSystemDto dto = new("healthy", services, version, memoryMb, 0);

        return Task.FromResult(Result.Success(dto));
    }
}
