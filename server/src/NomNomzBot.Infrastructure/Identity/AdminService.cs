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
using NomNomzBot.Domain.Identity.Entities;

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
        string? search,
        PaginationParams pagination,
        CancellationToken ct = default,
        bool? isLive = null
    )
    {
        IQueryable<Channel> channels = _db.Channels;
        if (!string.IsNullOrWhiteSpace(search))
        {
            string normalizedSearch = search.Trim().ToLowerInvariant();
            channels = channels.Where(c =>
                c.NameNormalized.Contains(normalizedSearch)
                || c.User.DisplayName.ToLower().Contains(normalizedSearch)
            );
        }

        if (isLive is { } live)
            channels = channels.Where(c => c.IsLive == live);

        // Ordering is chosen from a CLOSED set, never composed from the caller's string: an admin list is
        // exactly the surface where an arbitrary field name would become a way to probe the schema. An
        // unrecognised value falls back to the default rather than failing the request — a stale bookmark
        // must not 400.
        AdminListSort order = ParseSort(pagination.SortBy);

        IQueryable<Channel> ordered = order switch
        {
            AdminListSort.Oldest => channels.OrderBy(c => c.CreatedAt),
            AdminListSort.Name => channels.OrderBy(c => c.NameNormalized),
            _ => channels.OrderByDescending(c => c.CreatedAt),
        };

        int total = await channels.CountAsync(ct);

        List<AdminChannelDto> items = await (
            from c in ordered
            join sub in _db.ChannelSubscriptions on c.Id equals sub.BroadcasterId into subs
            from sub in subs.OrderByDescending(s => s.CreatedAt).Take(1).DefaultIfEmpty()
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
        string? search,
        PaginationParams pagination,
        CancellationToken ct = default,
        string? role = null
    )
    {
        // Only real bot USERS — operators/streamers/mods who authenticate and use the dashboard (they have an
        // AuthSession) or own a channel, plus platform staff — never the flood of auto-created chatter rows, the
        // bot accounts themselves, or anonymized users. This list backs the admin console AND the "act as" support
        // impersonation, which exists to reproduce/control what a real bot user experiences, not a random chatter.
        IQueryable<User> users = _db.Users.Where(u =>
            !u.IsBot
            && !u.IsAnonymized
            && (
                u.IsPlatformPrincipal
                || _db.Channels.Any(c => c.OwnerUserId == u.Id)
                || _db.AuthSessions.Any(s => s.UserId == u.Id)
            )
        );

        if (!string.IsNullOrWhiteSpace(search))
        {
            string normalizedSearch = search.Trim().ToLowerInvariant();
            users = users.Where(u =>
                u.UsernameNormalized.Contains(normalizedSearch)
                || u.DisplayName.ToLower().Contains(normalizedSearch)
            );
        }

        // The role filter uses the same derivation the DTO reports, so what an operator filters on is
        // exactly what they then read on the row. Anything else is a filter that appears to lie.
        if (!string.IsNullOrWhiteSpace(role))
        {
            bool wantsAdmin = role.Trim().Equals("admin", StringComparison.OrdinalIgnoreCase);
            users = users.Where(u => u.IsPlatformPrincipal == wantsAdmin);
        }

        AdminListSort userOrder = ParseSort(pagination.SortBy);

        IQueryable<User> orderedUsers = userOrder switch
        {
            AdminListSort.Oldest => users.OrderBy(u => u.CreatedAt),
            AdminListSort.Name => users.OrderBy(u => u.UsernameNormalized),
            _ => users.OrderByDescending(u => u.CreatedAt),
        };

        int total = await users.CountAsync(ct);

        List<AdminUserDto> items = await orderedUsers
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

    /// <summary>
    /// The closed set of orderings the admin lists offer. Deliberately small: every entry here is a
    /// column an operator can already see, so a sort can never reveal something the list does not show.
    /// </summary>
    private enum AdminListSort
    {
        Newest,
        Oldest,
        Name,
    }

    private static AdminListSort ParseSort(string? sort) =>
        sort?.Trim().ToLowerInvariant() switch
        {
            "oldest" => AdminListSort.Oldest,
            "name" => AdminListSort.Name,
            _ => AdminListSort.Newest,
        };
}
