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
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Webhooks.Entities;

namespace NomNomzBot.Infrastructure.Identity;

public sealed class AdminService : IAdminService
{
    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly HealthCheckService _healthChecks;
    private readonly IPlatformBotReadinessGate _botReadiness;
    private readonly IOutboundWebhookDispatcher _webhookDispatcher;
    private readonly IScheduledPipelineService _scheduledPipelines;

    public AdminService(
        IApplicationDbContext db,
        TimeProvider timeProvider,
        HealthCheckService healthChecks,
        IPlatformBotReadinessGate botReadiness,
        IOutboundWebhookDispatcher webhookDispatcher,
        IScheduledPipelineService scheduledPipelines
    )
    {
        _db = db;
        _timeProvider = timeProvider;
        _healthChecks = healthChecks;
        _botReadiness = botReadiness;
        _webhookDispatcher = webhookDispatcher;
        _scheduledPipelines = scheduledPipelines;
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

    public async Task<Result<PagedList<AdminEventSubTenantHealthDto>>> GetEventSubHealthAsync(
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        // Platform-plane rows (BroadcasterId == Guid.Empty, e.g. the bot's own user.whisper.message) belong to
        // no tenant, so they are excluded from a PER-TENANT page — grouped elsewhere would misrepresent them
        // as belonging to "channel zero".
        List<Guid> tenantIds = await _db
            .EventSubSubscriptions.Where(s => s.BroadcasterId != Guid.Empty)
            .Select(s => s.BroadcasterId)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync(ct);

        int total = tenantIds.Count;
        List<Guid> pageIds = tenantIds
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToList();

        List<EventSubSubscription> rows = await _db
            .EventSubSubscriptions.Where(s => pageIds.Contains(s.BroadcasterId))
            .OrderBy(s => s.EventType)
            .ToListAsync(ct);

        Dictionary<Guid, string> channelNames = await _db
            .Channels.Where(c => pageIds.Contains(c.Id))
            .Select(c => new { c.Id, c.User.DisplayName })
            .ToDictionaryAsync(c => c.Id, c => c.DisplayName, ct);

        List<AdminEventSubTenantHealthDto> items =
        [
            .. pageIds.Select(id => new AdminEventSubTenantHealthDto(
                id,
                channelNames.GetValueOrDefault(id, id.ToString()),
                [
                    .. rows.Where(r => r.BroadcasterId == id)
                        .Select(r => new AdminEventSubTopicHealthDto(
                            r.Id,
                            r.EventType,
                            r.Version,
                            r.Status,
                            r.Enabled,
                            r.LastError,
                            r.UpdatedAt
                        )),
                ]
            )),
        ];

        return Result.Success(
            new PagedList<AdminEventSubTenantHealthDto>(
                items,
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    public async Task<Result<PagedList<AdminWebhookDeliveryDto>>> GetWebhookDeliveryLogAsync(
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        int total = await _db.OutboundWebhookDeliveries.CountAsync(ct);

        // IgnoreQueryFilters on the endpoint join: a delivery whose endpoint was later soft-deleted must still
        // show up (labelled, not vanished) — the log is a history, not a live view of current endpoints.
        List<AdminWebhookDeliveryDto> items = await (
            from d in _db.OutboundWebhookDeliveries
            join e in _db.OutboundWebhookEndpoints.IgnoreQueryFilters()
                on d.EndpointId equals e.Id
                into endpoints
            from e in endpoints.DefaultIfEmpty()
            orderby d.CreatedAt descending, d.Id descending
            select new AdminWebhookDeliveryDto(
                d.Id,
                d.BroadcasterId,
                d.EndpointId,
                e != null && e.DeletedAt == null ? e.Name : "(deleted endpoint)",
                e != null && e.DeletedAt == null && e.IsEnabled,
                d.EventType,
                d.Attempt,
                d.Status.ToString(),
                d.ResponseCode,
                d.DurationMs,
                d.Error,
                d.CreatedAt
            )
        )
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        return Result.Success(
            new PagedList<AdminWebhookDeliveryDto>(
                items,
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    public async Task<Result<AdminWebhookReplayResultDto>> ReplayWebhookDeliveryAsync(
        long deliveryId,
        Guid actorUserId,
        CancellationToken ct = default
    )
    {
        OutboundWebhookDelivery? original = await _db.OutboundWebhookDeliveries.FirstOrDefaultAsync(
            d => d.Id == deliveryId,
            ct
        );
        if (original is null)
            return Result.Failure<AdminWebhookReplayResultDto>("Delivery not found.", "NOT_FOUND");

        Result<OutboundWebhookDelivery> replayResult = await _webhookDispatcher.ReplayDeliveryAsync(
            original,
            ct
        );
        if (replayResult.IsFailure)
            return Result.Failure<AdminWebhookReplayResultDto>(
                replayResult.ErrorMessage,
                replayResult.ErrorCode
            );

        OutboundWebhookDelivery replay = replayResult.Value;

        // Every replay is an outbound side effect against someone else's endpoint — audited unconditionally,
        // naming the acting operator (S-CONSEQ).
        _db.IamAuditLogs.Add(
            new IamAuditLog
            {
                PrincipalId = actorUserId,
                PrincipalType = IamPrincipalType.Employee,
                Permission = "webhook:replay",
                TargetBroadcasterId = original.BroadcasterId,
                TargetResource = original.EndpointId.ToString(),
                Justification =
                    $"actor={actorUserId};originalDeliveryId={original.Id};newDeliveryId={replay.Id};eventType={original.EventType}",
                BreakGlass = false,
                Outcome = IamOutcome.Allowed,
                OccurredAt = _timeProvider.GetUtcNow().UtcDateTime,
            }
        );
        await _db.SaveChangesAsync(ct);

        return Result.Success(
            new AdminWebhookReplayResultDto(
                original.Id,
                replay.Id,
                replay.Status.ToString(),
                replay.ResponseCode
            )
        );
    }

    public async Task<Result<PagedList<AdminScheduledJobDto>>> GetScheduledJobQueueAsync(
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        int total = await _db.ScheduledPipelineTasks.CountAsync(ct);

        List<ScheduledPipelineTask> page = await _db
            .ScheduledPipelineTasks.OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        List<Guid> broadcasterIds = page.Select(t => t.BroadcasterId).Distinct().ToList();
        List<Guid> pipelineIds = page.Select(t => t.PipelineId).Distinct().ToList();

        Dictionary<Guid, string> channelNames = await _db
            .Channels.Where(c => broadcasterIds.Contains(c.Id))
            .Select(c => new { c.Id, c.User.DisplayName })
            .ToDictionaryAsync(c => c.Id, c => c.DisplayName, ct);

        // IgnoreQueryFilters: a task whose pipeline was soft-deleted must still resolve to "gone", not silently
        // fall out of the join and read as "exists" by omission.
        HashSet<Guid> existingPipelineIds =
        [
            .. await _db
                .Pipelines.IgnoreQueryFilters()
                .Where(p => pipelineIds.Contains(p.Id) && p.DeletedAt == null)
                .Select(p => p.Id)
                .ToListAsync(ct),
        ];

        DateTimeOffset now = _timeProvider.GetUtcNow();

        List<AdminScheduledJobDto> items =
        [
            .. page.Select(t =>
            {
                bool pipelineExists = existingPipelineIds.Contains(t.PipelineId);
                return new AdminScheduledJobDto(
                    t.Id,
                    t.BroadcasterId,
                    channelNames.GetValueOrDefault(t.BroadcasterId, t.BroadcasterId.ToString()),
                    t.PipelineId,
                    t.PipelineName,
                    pipelineExists,
                    t.Status,
                    ToDisplayState(t, now),
                    t.DueAt.UtcDateTime,
                    t.FiredAt?.UtcDateTime,
                    t.CreatedAt.UtcDateTime,
                    t.TriggeredByDisplayName,
                    t.Status == ScheduledPipelineTaskStatus.Expired && pipelineExists
                );
            }),
        ];

        return Result.Success(
            new PagedList<AdminScheduledJobDto>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    private static string ToDisplayState(ScheduledPipelineTask task, DateTimeOffset now) =>
        task.Status switch
        {
            ScheduledPipelineTaskStatus.Pending when task.DueAt <= now => "running",
            ScheduledPipelineTaskStatus.Pending => "queued",
            ScheduledPipelineTaskStatus.Fired => "succeeded",
            ScheduledPipelineTaskStatus.Expired => "failed",
            ScheduledPipelineTaskStatus.Cancelled => "cancelled",
            _ => task.Status,
        };

    public async Task<Result<AdminScheduledJobRetryResultDto>> RetryScheduledJobAsync(
        Guid taskId,
        Guid actorUserId,
        CancellationToken ct = default
    )
    {
        ScheduledPipelineTask? original = await _db.ScheduledPipelineTasks.FirstOrDefaultAsync(
            t => t.Id == taskId,
            ct
        );
        if (original is null)
            return Result.Failure<AdminScheduledJobRetryResultDto>(
                "Scheduled job not found.",
                "NOT_FOUND"
            );

        if (original.Status != ScheduledPipelineTaskStatus.Expired)
        {
            string reason = original.Status switch
            {
                ScheduledPipelineTaskStatus.Fired =>
                    "This job already ran successfully; there is nothing to retry.",
                ScheduledPipelineTaskStatus.Pending =>
                    "This job has not failed — it is still queued to run.",
                ScheduledPipelineTaskStatus.Cancelled =>
                    "This job was cancelled, not failed; it cannot be retried.",
                _ => "This job is not in a retryable state.",
            };
            return Result.Failure<AdminScheduledJobRetryResultDto>(reason, "NOT_RETRYABLE");
        }

        // IgnoreQueryFilters: an admin retry runs cross-tenant with no ambient tenant, and the pipeline row
        // itself carries the soft-delete flag this check needs to see even if it were otherwise filtered.
        bool pipelineExists = await _db
            .Pipelines.IgnoreQueryFilters()
            .AnyAsync(p => p.Id == original.PipelineId && p.DeletedAt == null, ct);
        if (!pipelineExists)
            return Result.Failure<AdminScheduledJobRetryResultDto>(
                "The target pipeline no longer exists; this job cannot be retried.",
                "TARGET_GONE"
            );

        Dictionary<string, string> variables;
        try
        {
            variables =
                JsonSerializer.Deserialize<Dictionary<string, string>>(original.VariablesJson)
                ?? [];
        }
        catch (JsonException)
        {
            variables = [];
        }

        Result<ScheduledPipelineTaskDto> retried = await _scheduledPipelines.ScheduleAsync(
            original.BroadcasterId,
            original.PipelineId,
            1,
            variables,
            original.TriggeredByUserId,
            original.TriggeredByDisplayName,
            null,
            ct
        );
        if (retried.IsFailure)
            return Result.Failure<AdminScheduledJobRetryResultDto>(
                retried.ErrorMessage,
                retried.ErrorCode
            );

        // Retrying a job is an outbound side effect against a tenant's pipeline — audited unconditionally,
        // naming the acting operator (S-CONSEQ), mirroring ReplayWebhookDeliveryAsync.
        _db.IamAuditLogs.Add(
            new IamAuditLog
            {
                PrincipalId = actorUserId,
                PrincipalType = IamPrincipalType.Employee,
                Permission = "scheduled_job:retry",
                TargetBroadcasterId = original.BroadcasterId,
                TargetResource = original.Id.ToString(),
                Justification =
                    $"actor={actorUserId};originalTaskId={original.Id};newTaskId={retried.Value.Id};pipelineId={original.PipelineId}",
                BreakGlass = false,
                Outcome = IamOutcome.Allowed,
                OccurredAt = _timeProvider.GetUtcNow().UtcDateTime,
            }
        );
        await _db.SaveChangesAsync(ct);

        return Result.Success(
            new AdminScheduledJobRetryResultDto(
                original.Id,
                retried.Value.Id,
                original.PipelineId,
                original.PipelineName
                    ?? retried.Value.PipelineName
                    ?? original.PipelineId.ToString(),
                retried.Value.DueAt.UtcDateTime
            )
        );
    }

    public async Task<Result<PagedList<AdminTenantUsageDto>>> GetTenantUsageAsync(
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        List<Guid> tenantIds = await _db
            .UsageRecords.Select(u => u.BroadcasterId)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync(ct);

        int total = tenantIds.Count;
        List<Guid> pageIds = tenantIds
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToList();

        Dictionary<Guid, string> channelNames = await _db
            .Channels.Where(c => pageIds.Contains(c.Id))
            .Select(c => new { c.Id, c.User.DisplayName })
            .ToDictionaryAsync(c => c.Id, c => c.DisplayName, ct);

        List<AdminTenantUsageDto> items = [];
        foreach (Guid tenantId in pageIds)
        {
            // Explicit per-tenant filter (not the ambient query filter, which is a no-op for a cross-tenant
            // admin scope) — the isolation this figure depends on is enforced right here in the predicate.
            List<UsageRecord> records = await _db
                .UsageRecords.Where(u => u.BroadcasterId == tenantId)
                .ToListAsync(ct);

            // The most recent metering period this tenant has any recorded usage for — the window the
            // reported figures cover, stated rather than assumed.
            DateTime periodStart = records.Max(r => r.PeriodStart);
            DateTime periodEnd = records.First(r => r.PeriodStart == periodStart).PeriodEnd;

            List<AdminTenantUsageMetricDto> metrics =
            [
                .. records
                    .Where(r => r.PeriodStart == periodStart)
                    .GroupBy(r => r.MetricKey)
                    .Select(g => new AdminTenantUsageMetricDto(g.Key, g.Sum(r => r.Quantity))),
            ];

            long ttsCharacters = await _db
                .TtsUsageRecords.Where(t =>
                    t.BroadcasterId == tenantId
                    && t.OccurredAt >= periodStart
                    && t.OccurredAt < periodEnd
                )
                .SumAsync(t => (long)t.CharacterCount, ct);

            items.Add(
                new AdminTenantUsageDto(
                    tenantId,
                    channelNames.GetValueOrDefault(tenantId, tenantId.ToString()),
                    periodStart,
                    periodEnd,
                    metrics,
                    ttsCharacters
                )
            );
        }

        return Result.Success(
            new PagedList<AdminTenantUsageDto>(items, pagination.Page, pagination.PageSize, total)
        );
    }

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
