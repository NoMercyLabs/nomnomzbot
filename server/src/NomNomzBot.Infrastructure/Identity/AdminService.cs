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
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Billing.Entities;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Infrastructure.Billing;
using NomNomzBot.Infrastructure.EventStore;

namespace NomNomzBot.Infrastructure.Identity;

public sealed class AdminService : IAdminService
{
    /// <summary>
    /// The policy threshold an error budget is measured against — a stated decision, not a measured
    /// quantity. The allowed error rate it implies (1 - this) is the denominator every tenant's real error
    /// rate is divided by to report remaining budget.
    /// </summary>
    private const double ErrorBudgetTargetSuccessRate = 0.99;

    /// <summary>Safety ceiling on a single admin replay — above this, narrow the window or event type first.
    /// A "2am tool" that could accidentally re-fold tens of thousands of events into a read model needs a
    /// hard stop, not an unbounded loop.</summary>
    private const int MaxReplayBatchSize = 2_000;

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly HealthCheckService _healthChecks;
    private readonly IPlatformBotReadinessGate _botReadiness;
    private readonly IOutboundWebhookDispatcher _webhookDispatcher;
    private readonly IScheduledPipelineService _scheduledPipelines;
    private readonly IEnumerable<IProjection> _projections;
    private readonly IEventUpcasterRegistry _upcasters;

    public AdminService(
        IApplicationDbContext db,
        TimeProvider timeProvider,
        HealthCheckService healthChecks,
        IPlatformBotReadinessGate botReadiness,
        IOutboundWebhookDispatcher webhookDispatcher,
        IScheduledPipelineService scheduledPipelines,
        IEnumerable<IProjection> projections,
        IEventUpcasterRegistry upcasters
    )
    {
        _db = db;
        _timeProvider = timeProvider;
        _healthChecks = healthChecks;
        _botReadiness = botReadiness;
        _webhookDispatcher = webhookDispatcher;
        _scheduledPipelines = scheduledPipelines;
        _projections = projections;
        _upcasters = upcasters;
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

        // The owner-authored price catalogue (S-ADMIN-4d), loaded once for the whole page. A unit key with
        // no entry here is UNPRICED — every join below treats "missing from this dictionary" as "no cost
        // figure exists", never as "costs zero".
        Dictionary<string, PricedUnit> pricesByUnitKey = await _db
            .PricedUnits.Where(p => p.DeletedAt == null)
            .ToDictionaryAsync(p => p.UnitKey, ct);

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

            List<string> unpricedUnitKeys = [];
            Dictionary<string, long> costsByCurrency = [];

            List<AdminTenantUsageMetricDto> metrics =
            [
                .. records
                    .Where(r => r.PeriodStart == periodStart)
                    .GroupBy(r => r.MetricKey)
                    .Select(g => (MetricKey: g.Key, Quantity: g.Sum(r => r.Quantity)))
                    .Select(m =>
                    {
                        if (!pricesByUnitKey.TryGetValue(m.MetricKey, out PricedUnit? price))
                        {
                            unpricedUnitKeys.Add(m.MetricKey);
                            return new AdminTenantUsageMetricDto(m.MetricKey, m.Quantity);
                        }

                        long cost = PricedUnitCostCalculator.ComputeCostMinorUnits(
                            m.Quantity,
                            price
                        );
                        costsByCurrency[price.Currency] =
                            costsByCurrency.GetValueOrDefault(price.Currency) + cost;
                        return new AdminTenantUsageMetricDto(
                            m.MetricKey,
                            m.Quantity,
                            cost,
                            price.Currency
                        );
                    }),
            ];

            long ttsCharacters = await _db
                .TtsUsageRecords.Where(t =>
                    t.BroadcasterId == tenantId
                    && t.OccurredAt >= periodStart
                    && t.OccurredAt < periodEnd
                )
                .SumAsync(t => (long)t.CharacterCount, ct);

            long? ttsCostMinorUnits = null;
            string? ttsCurrency = null;
            if (ttsCharacters > 0)
            {
                if (
                    pricesByUnitKey.TryGetValue(
                        PricedUnitKeys.TtsCharacters,
                        out PricedUnit? ttsPrice
                    )
                )
                {
                    ttsCostMinorUnits = PricedUnitCostCalculator.ComputeCostMinorUnits(
                        ttsCharacters,
                        ttsPrice
                    );
                    ttsCurrency = ttsPrice.Currency;
                    costsByCurrency[ttsPrice.Currency] =
                        costsByCurrency.GetValueOrDefault(ttsPrice.Currency)
                        + ttsCostMinorUnits.Value;
                }
                else
                {
                    unpricedUnitKeys.Add(PricedUnitKeys.TtsCharacters);
                }
            }

            items.Add(
                new AdminTenantUsageDto(
                    tenantId,
                    channelNames.GetValueOrDefault(tenantId, tenantId.ToString()),
                    periodStart,
                    periodEnd,
                    metrics,
                    ttsCharacters,
                    ttsCostMinorUnits,
                    ttsCurrency,
                    [.. costsByCurrency.Select(c => new AdminTenantUsageCostDto(c.Key, c.Value))],
                    unpricedUnitKeys
                )
            );
        }

        return Result.Success(
            new PagedList<AdminTenantUsageDto>(items, pagination.Page, pagination.PageSize, total)
        );
    }

    public async Task<Result<PagedList<AdminTenantErrorBudgetDto>>> GetErrorBudgetAsync(
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        DateTime windowEnd = _timeProvider.GetUtcNow().UtcDateTime;
        DateTime windowStart = windowEnd.AddHours(-24);

        // Only tenants with at least one RESOLVED attempt in the window — a tenant with nothing measured yet
        // has no budget to report, never a fabricated 0%. Pending attempts have not resolved either way.
        List<Guid> tenantIds = await _db
            .OutboundWebhookDeliveries.Where(d =>
                d.CreatedAt >= windowStart
                && d.CreatedAt <= windowEnd
                && d.Status != WebhookDeliveryStatus.Pending
            )
            .Select(d => d.BroadcasterId)
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

        double allowedErrorRate = 1 - ErrorBudgetTargetSuccessRate;
        List<AdminTenantErrorBudgetDto> items = [];
        foreach (Guid tenantId in pageIds)
        {
            // Explicit per-tenant filter (not the ambient query filter, which is a no-op for a cross-tenant
            // admin scope) — mirrors GetTenantUsageAsync: the isolation this figure depends on is enforced
            // right here, so one tenant's failures can never bleed into another's count.
            List<WebhookDeliveryStatus> statuses = await _db
                .OutboundWebhookDeliveries.Where(d =>
                    d.BroadcasterId == tenantId
                    && d.CreatedAt >= windowStart
                    && d.CreatedAt <= windowEnd
                    && d.Status != WebhookDeliveryStatus.Pending
                )
                .Select(d => d.Status)
                .ToListAsync(ct);

            long attempts = statuses.Count;
            long errors = statuses.Count(s =>
                s is WebhookDeliveryStatus.Failed or WebhookDeliveryStatus.DeadLetter
            );

            double? errorRate = attempts == 0 ? null : (double)errors / attempts;
            double? budgetRemaining = errorRate is null
                ? null
                : 1 - (errorRate.Value / allowedErrorRate);

            items.Add(
                new AdminTenantErrorBudgetDto(
                    tenantId,
                    channelNames.GetValueOrDefault(tenantId, tenantId.ToString()),
                    windowStart,
                    windowEnd,
                    attempts,
                    errors,
                    errorRate,
                    ErrorBudgetTargetSuccessRate,
                    budgetRemaining
                )
            );
        }

        return Result.Success(
            new PagedList<AdminTenantErrorBudgetDto>(
                items,
                pagination.Page,
                pagination.PageSize,
                total
            )
        );
    }

    public Task<Result<IReadOnlyList<AdminReplayableProjectionDto>>> ListReplayableProjectionsAsync(
        CancellationToken ct = default
    )
    {
        List<AdminReplayableProjectionDto> dtos =
        [
            .. _projections
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => new AdminReplayableProjectionDto(
                    p.Name,
                    p.IsGlobal,
                    [.. p.SubscribedEventTypes.OrderBy(t => t, StringComparer.Ordinal)]
                )),
        ];
        return Task.FromResult(Result.Success<IReadOnlyList<AdminReplayableProjectionDto>>(dtos));
    }

    public async Task<Result<AdminEventReplayPreviewDto>> PreviewEventReplayAsync(
        Guid broadcasterId,
        string projectionName,
        DateTime fromUtc,
        DateTime toUtc,
        string? eventType,
        CancellationToken ct = default
    )
    {
        if (toUtc < fromUtc)
            return Result.Failure<AdminEventReplayPreviewDto>(
                "The replay window's end must not precede its start.",
                "INVALID_WINDOW"
            );

        Result<IProjection> resolved = ResolveProjection(projectionName);
        if (resolved.IsFailure)
            return Result.Failure<AdminEventReplayPreviewDto>(
                resolved.ErrorMessage!,
                resolved.ErrorCode
            );

        Result<string?> typeCheck = ValidateEventType(resolved.Value, eventType);
        if (typeCheck.IsFailure)
            return Result.Failure<AdminEventReplayPreviewDto>(
                typeCheck.ErrorMessage!,
                typeCheck.ErrorCode
            );

        long count = await MatchingEvents(broadcasterId, fromUtc, toUtc, eventType, resolved.Value)
            .CountAsync(ct);

        return Result.Success(
            new AdminEventReplayPreviewDto(
                broadcasterId,
                projectionName,
                fromUtc,
                toUtc,
                eventType,
                count
            )
        );
    }

    public async Task<Result<AdminEventReplayResultDto>> ExecuteEventReplayAsync(
        Guid broadcasterId,
        string projectionName,
        DateTime fromUtc,
        DateTime toUtc,
        string? eventType,
        long expectedCount,
        Guid actorUserId,
        CancellationToken ct = default
    )
    {
        if (toUtc < fromUtc)
            return Result.Failure<AdminEventReplayResultDto>(
                "The replay window's end must not precede its start.",
                "INVALID_WINDOW"
            );

        Result<IProjection> resolved = ResolveProjection(projectionName);
        if (resolved.IsFailure)
            return Result.Failure<AdminEventReplayResultDto>(
                resolved.ErrorMessage!,
                resolved.ErrorCode
            );
        IProjection projection = resolved.Value;

        Result<string?> typeCheck = ValidateEventType(projection, eventType);
        if (typeCheck.IsFailure)
            return Result.Failure<AdminEventReplayResultDto>(
                typeCheck.ErrorMessage!,
                typeCheck.ErrorCode
            );

        List<EventJournal> matches = await MatchingEvents(
                broadcasterId,
                fromUtc,
                toUtc,
                eventType,
                projection
            )
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.StreamPosition)
            .ToListAsync(ct);

        // Fail closed: the operator may only ever act on the SAME count the preview showed them. A stale
        // preview — more events landed since, or someone narrowed the scope — refuses outright rather than
        // silently replaying a different set than the one that was confirmed (the counted-preview +
        // fail-closed-on-stale-count pattern).
        if (matches.Count != expectedCount)
            return Result.Failure<AdminEventReplayResultDto>(
                $"The replay scope now matches {matches.Count} event(s), not the {expectedCount} shown in "
                    + "the preview. Refresh the preview and try again.",
                "STALE_COUNT"
            );

        if (matches.Count > MaxReplayBatchSize)
            return Result.Failure<AdminEventReplayResultDto>(
                $"This scope matches {matches.Count} events, above the {MaxReplayBatchSize}-event replay "
                    + "ceiling. Narrow the window or event type and try again.",
                "SCOPE_TOO_LARGE"
            );

        long applied = 0;
        foreach (EventJournal row in matches)
        {
            EventRecord record = EventJournalService.Map(row);

            Result<UpcastResult> upcast = _upcasters.UpcastToCurrent(
                record.EventType,
                record.EventVersion,
                record.PayloadJson
            );
            if (upcast.IsFailure)
                return Result.Failure<AdminEventReplayResultDto>(
                    upcast.ErrorMessage!,
                    upcast.ErrorCode
                );

            EventRecord current = upcast.Value.Changed
                ? record with
                {
                    PayloadJson = upcast.Value.PayloadJson,
                    EventVersion = upcast.Value.ToVersion,
                }
                : record;

            Result apply = await projection.ApplyAsync(current, ct);
            if (apply.IsFailure)
                return Result.Failure<AdminEventReplayResultDto>(
                    apply.ErrorMessage!,
                    apply.ErrorCode
                );

            applied++;
        }

        // Replaying events into a projection is a side effect with real blast radius — audited
        // unconditionally, naming the acting operator, the exact scope, and the count applied (S-CONSEQ),
        // mirroring ReplayWebhookDeliveryAsync/RetryScheduledJobAsync.
        _db.IamAuditLogs.Add(
            new IamAuditLog
            {
                PrincipalId = actorUserId,
                PrincipalType = IamPrincipalType.Employee,
                Permission = "eventstore:admin-replay",
                TargetBroadcasterId = broadcasterId,
                TargetResource = projectionName,
                Justification =
                    $"actor={actorUserId};projection={projectionName};from={fromUtc:O};to={toUtc:O};"
                    + $"eventType={eventType ?? "(any)"};count={applied}",
                BreakGlass = false,
                Outcome = IamOutcome.Allowed,
                OccurredAt = _timeProvider.GetUtcNow().UtcDateTime,
            }
        );
        await _db.SaveChangesAsync(ct);

        return Result.Success(
            new AdminEventReplayResultDto(
                broadcasterId,
                projectionName,
                fromUtc,
                toUtc,
                eventType,
                applied
            )
        );
    }

    private Result<IProjection> ResolveProjection(string projectionName)
    {
        IProjection? projection = _projections.FirstOrDefault(p => p.Name == projectionName);
        return projection is null
            ? Result.Failure<IProjection>(
                $"No projection registered with name '{projectionName}'.",
                "PROJECTION_NOT_FOUND"
            )
            : Result.Success(projection);
    }

    private static Result<string?> ValidateEventType(IProjection projection, string? eventType)
    {
        if (
            eventType is not null
            && projection.SubscribedEventTypes.Count > 0
            && !projection.SubscribedEventTypes.Contains(eventType)
        )
            return Result.Failure<string?>(
                $"Projection '{projection.Name}' does not subscribe to event type '{eventType}'.",
                "EVENT_TYPE_NOT_SUBSCRIBED"
            );
        return Result.Success(eventType);
    }

    private IQueryable<EventJournal> MatchingEvents(
        Guid broadcasterId,
        DateTime fromUtc,
        DateTime toUtc,
        string? eventType,
        IProjection projection
    )
    {
        IQueryable<EventJournal> query = _db
            .EventJournals.AsNoTracking()
            .Where(e =>
                e.BroadcasterId == broadcasterId && e.OccurredAt >= fromUtc && e.OccurredAt <= toUtc
            );

        if (eventType is not null)
            return query.Where(e => e.EventType == eventType);

        // No explicit type filter: narrow to exactly what this projection consumes, so the previewed count
        // matches the count that will actually reach ApplyAsync — never a wider promise than what executes.
        if (projection.SubscribedEventTypes.Count > 0)
        {
            List<string> subscribedTypes = [.. projection.SubscribedEventTypes];
            query = query.Where(e => subscribedTypes.Contains(e.EventType));
        }

        return query;
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
