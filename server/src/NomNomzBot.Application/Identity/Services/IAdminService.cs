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
using NomNomzBot.Application.Identity.Dtos;

namespace NomNomzBot.Application.Identity.Services;

public interface IAdminService
{
    Task<Result<AdminStatsDto>> GetStatsAsync(CancellationToken ct = default);

    /// <summary>Paged channel listing, optionally narrowed by a case-insensitive [search] match against the
    /// channel's login/name or its owner's display name.</summary>
    Task<Result<PagedList<AdminChannelDto>>> ListChannelsAsync(
        string? search,
        PaginationParams pagination,
        CancellationToken ct = default,
        bool? isLive = null
    );

    /// <summary>Paged user listing, optionally narrowed by a case-insensitive [search] match against the
    /// user's login or display name.</summary>
    Task<Result<PagedList<AdminUserDto>>> ListUsersAsync(
        string? search,
        PaginationParams pagination,
        CancellationToken ct = default,
        string? role = null
    );

    Task<Result<AdminSystemDto>> GetSystemHealthAsync(CancellationToken ct = default);

    /// <summary>
    /// The real EventSub registry (S-ADMIN-6a), grouped by tenant — one page of BROADCASTERS, each carrying
    /// every topic <c>TwitchEventSubHostedService</c> has a row for. Reads <c>EventSubSubscriptions</c>
    /// directly; never fabricates a topic or a status the registry does not actually hold.
    /// </summary>
    Task<Result<PagedList<AdminEventSubTenantHealthDto>>> GetEventSubHealthAsync(
        PaginationParams pagination,
        CancellationToken ct = default
    );

    /// <summary>Cross-tenant outbound webhook delivery log (S-ADMIN-6a), newest attempt first, paged.</summary>
    Task<Result<PagedList<AdminWebhookDeliveryDto>>> GetWebhookDeliveryLogAsync(
        PaginationParams pagination,
        CancellationToken ct = default
    );

    /// <summary>
    /// Replays one delivery: sends a brand-new attempt carrying the original's exact rendered body, appended
    /// as its own row — the delivery being replayed is never mutated. Refused (NOT_FOUND/ENDPOINT_DISABLED)
    /// when the endpoint has since been deleted or disabled. Always written to the platform audit log,
    /// naming the acting operator.
    /// </summary>
    Task<Result<AdminWebhookReplayResultDto>> ReplayWebhookDeliveryAsync(
        long deliveryId,
        Guid actorUserId,
        CancellationToken ct = default
    );

    /// <summary>
    /// The REAL background job queue (S-ADMIN-6b) — every <c>ScheduledPipelineTask</c> row, newest first,
    /// paged. Never a fabricated queue: this is the exact primitive <c>ScheduledPipelineExpiryService</c>
    /// sweeps and dispatches.
    /// </summary>
    Task<Result<PagedList<AdminScheduledJobDto>>> GetScheduledJobQueueAsync(
        PaginationParams pagination,
        CancellationToken ct = default
    );

    /// <summary>
    /// Retries one failed (expired) scheduled job by scheduling a brand-new deferred run for the same pipeline,
    /// due immediately — appended as its own row; the original failed attempt is never mutated. Refused
    /// (NOT_RETRYABLE) when the job already succeeded, is still queued, or was cancelled; refused (TARGET_GONE)
    /// when its pipeline no longer exists. Always audited, naming the acting operator.
    /// </summary>
    Task<Result<AdminScheduledJobRetryResultDto>> RetryScheduledJobAsync(
        Guid taskId,
        Guid actorUserId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Per-tenant usage for each tenant's most recent metering period (S-ADMIN-6b), computed purely from
    /// recorded <c>UsageRecord</c> + <c>TtsUsageRecord</c> rows — never a fabricated figure. One tenant's usage
    /// never counts toward another's.
    /// </summary>
    Task<Result<PagedList<AdminTenantUsageDto>>> GetTenantUsageAsync(
        PaginationParams pagination,
        CancellationToken ct = default
    );
}
