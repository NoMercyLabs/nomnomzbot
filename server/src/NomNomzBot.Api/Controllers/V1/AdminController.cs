// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Api.RateLimiting;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Platform.Dtos;
using NomNomzBot.Application.Platform.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Platform-admin dashboard: aggregate stats, channels, users, system health, and events. Plane-C IAM gates
/// (roles-permissions.md §5.5 rewires this controller off the legacy admin-role check): the tenant listing
/// carries <c>tenant:read</c> (stream-admin.md §5 platform rows), aggregate stats carry
/// <c>platform:analytics:read</c> (analytics.md §5), and the remaining operator reads carry <c>iam:manage</c>
/// (no dedicated spec row — see OWNER-CONFIRM).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admin")]
[Authorize]
[Tags("Admin")]
public class AdminController : BaseController
{
    private readonly IAdminService _adminService;
    private readonly IApplicationDbContext _db;
    private readonly IDekRotationService _dekRotationService;
    private readonly IProviderCredentialService _providerCredentials;
    private readonly ICurrentUserService _currentUser;

    public AdminController(
        IAdminService adminService,
        IApplicationDbContext db,
        IDekRotationService dekRotationService,
        IProviderCredentialService providerCredentials,
        ICurrentUserService currentUser
    )
    {
        _adminService = adminService;
        _db = db;
        _dekRotationService = dekRotationService;
        _providerCredentials = providerCredentials;
        _currentUser = currentUser;
    }

    public record ServiceHealthResponseDto(string Name, string Status);

    public record RotateEncryptionKeyRequestDto(string PreviousKey, string CurrentKey);

    public record PlatformEventDto(string Message, string Time, string Type);

    /// <summary>Returns aggregate statistics for the admin dashboard.</summary>
    [HttpGet("stats")]
    [Authorize(Policy = IamPermissionKeys.PlatformAnalyticsRead)]
    [ProducesResponseType<StatusResponseDto<AdminStatsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAdminStats(CancellationToken ct)
    {
        Result<AdminStatsDto> result = await _adminService.GetStatsAsync(ct);
        return ResultResponse(result);
    }

    // ── Provider app credentials ─────────────────────────────────────────────

    /// <summary>
    /// Every provider's OAuth app credential state: the resolved client id, and for both fields whether the
    /// value in play is a stored one or the environment's.
    ///
    /// <para>The setup wizard writes these once and then has no further say; until now nothing could read
    /// back what was configured or rotate a leaked secret without editing the database by hand.</para>
    ///
    /// <para><b>No secret is ever returned</b> — only whether one exists and which source wins. The client
    /// id is returned because it is a public identifier that appears in every OAuth URL a viewer's browser
    /// already sees, and withholding it would only stop the operator checking the value most likely wrong.</para>
    /// </summary>
    [HttpGet("providers")]
    [EnableRateLimiting(RateLimitPolicyNames.Read)]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<ProviderCredentialDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> ListProviderCredentials(CancellationToken ct) =>
        ResultResponse(await _providerCredentials.ListAsync(ct));

    /// <summary>
    /// Stores a client id and/or secret for one provider. A blank field is left untouched, so rotating a
    /// secret needs no id and a half-filled form cannot wipe a working credential.
    /// </summary>
    [HttpPut("providers/{provider}")]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    [ProducesResponseType<StatusResponseDto<ProviderCredentialDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveProviderCredential(
        string provider,
        [FromBody] SaveProviderCredentialRequest request,
        CancellationToken ct
    ) => ResultResponse(await _providerCredentials.SaveAsync(provider, request, ct));

    /// <summary>
    /// Clears a provider's STORED credentials, handing resolution back to the environment.
    ///
    /// <para>Destructive, and the repair path for a real failure: a stored secret shadows the environment,
    /// so an operator who fixes a rotated secret in their <c>.env</c> keeps getting 401s from a stale stored
    /// value they cannot see. It is a separate verb precisely so it can never happen by accident.</para>
    /// </summary>
    [HttpDelete("providers/{provider}")]
    // Counted, not estimated: the row the console already holds names which of the two fields are stored,
    // so the confirm states exactly how many stored values disappear and which source takes over. The
    // response returns the resulting state, so the count is verifiable after the fact too.
    [DestructiveAction(HasCountedBlastRadius = true)]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    [ProducesResponseType<StatusResponseDto<ProviderCredentialDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClearProviderCredential(
        string provider,
        CancellationToken ct
    ) => ResultResponse(await _providerCredentials.ClearAsync(provider, ct));

    /// <summary>Returns all channels with their current status.</summary>
    [HttpGet("channels")]
    [Authorize(Policy = IamPermissionKeys.TenantRead)]
    [ProducesResponseType<PaginatedResponse<AdminChannelDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListChannels(
        [FromQuery] string? search,
        [FromQuery] PageRequestDto request,
        CancellationToken ct,
        [FromQuery] bool? isLive = null
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<AdminChannelDto>> result = await _adminService.ListChannelsAsync(
            search,
            pagination,
            ct,
            isLive
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>
    /// Returns real bot USERS — operators/streamers/mods who authenticate and use the dashboard, or own a
    /// channel, plus platform staff. Auto-created chatter rows, bot accounts, and anonymized users are excluded,
    /// so this list (and the "act as" support impersonation built on it) targets people who actually use the bot.
    /// </summary>
    [HttpGet("users")]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    [ProducesResponseType<PaginatedResponse<AdminUserDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListUsers(
        [FromQuery] string? search,
        [FromQuery] PageRequestDto request,
        CancellationToken ct,
        [FromQuery] string? role = null
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<AdminUserDto>> result = await _adminService.ListUsersAsync(
            search,
            pagination,
            ct,
            role
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Returns system health and process metrics.</summary>
    [HttpGet("system")]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    [ProducesResponseType<StatusResponseDto<AdminSystemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSystemHealth(CancellationToken ct)
    {
        Result<AdminSystemDto> result = await _adminService.GetSystemHealthAsync(ct);
        return ResultResponse(result);
    }

    /// <summary>Returns service health list (for dashboard health panel).</summary>
    [HttpGet("health")]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    [ProducesResponseType<StatusResponseDto<List<ServiceHealthResponseDto>>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> GetHealth(CancellationToken ct)
    {
        Result<AdminSystemDto> result = await _adminService.GetSystemHealthAsync(ct);
        if (result.IsFailure)
            return ResultResponse(result);

        List<ServiceHealthResponseDto> services =
        [
            .. result.Value.Services.Select(s => new ServiceHealthResponseDto(s.Name, s.Status)),
        ];

        return Ok(new StatusResponseDto<List<ServiceHealthResponseDto>> { Data = services });
    }

    /// <summary>Returns recent platform events for the admin dashboard.</summary>
    [HttpGet("events")]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    [ProducesResponseType<StatusResponseDto<List<PlatformEventDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEvents(CancellationToken ct)
    {
        var events = await _db
            .ChannelEvents.OrderByDescending(e => e.CreatedAt)
            .Take(20)
            .Select(e => new
            {
                e.Type,
                e.CreatedAt,
                Username = e.User != null ? e.User.DisplayName : null,
            })
            .ToListAsync(ct);

        List<PlatformEventDto> dtos =
        [
            .. events.Select(e =>
            {
                string message = e.Username is not null ? $"{e.Username}: {e.Type}" : e.Type;

                string eventType =
                    e.Type.Contains("sub") ? "success"
                    : e.Type.Contains("ban") || e.Type.Contains("timeout") ? "warning"
                    : "info";

                return new PlatformEventDto(message, e.CreatedAt.ToString("HH:mm"), eventType);
            }),
        ];

        return Ok(new StatusResponseDto<List<PlatformEventDto>> { Data = dtos });
    }

    /// <summary>
    /// KEK-rotation re-wrap pass (gdpr-crypto): re-wraps every stored DEK from
    /// <see cref="RotateEncryptionKeyRequestDto.PreviousKey"/> to
    /// <see cref="RotateEncryptionKeyRequestDto.CurrentKey"/> so a rotated <c>Encryption:Key</c> does not
    /// orphan stored secrets. Idempotent — a second call re-wraps nothing.
    /// </summary>
    [HttpPost("security/rotate-encryption-key")]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    [ProducesResponseType<StatusResponseDto<DekRotationSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RotateEncryptionKey(
        [FromBody] RotateEncryptionKeyRequestDto request,
        CancellationToken ct
    )
    {
        Result<DekRotationSummary> result = await _dekRotationService.RotateAllDeksAsync(
            request.PreviousKey,
            request.CurrentKey,
            ct
        );
        return ResultResponse(result);
    }

    // ── 2am tools: EventSub health + outbound webhook delivery log (S-ADMIN-6a) ─────────────────

    /// <summary>
    /// Per-tenant EventSub subscription registry health: which topics are subscribed for which broadcaster,
    /// their REAL state, and when each was last confirmed — reads the exact registry
    /// <c>TwitchEventSubHostedService</c> re-registers on every reconnect. Never a fabricated list: a topic
    /// missing from a tenant's rows is simply absent, not padded with a guessed "healthy" entry.
    /// </summary>
    [HttpGet("eventsub/health")]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    [ProducesResponseType<PaginatedResponse<AdminEventSubTenantHealthDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEventSubHealth(
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<AdminEventSubTenantHealthDto>> result =
            await _adminService.GetEventSubHealthAsync(pagination, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Cross-tenant outbound webhook delivery log: every attempt, its status, response code, and
    /// timestamp — newest first, paged.</summary>
    [HttpGet("webhooks/deliveries")]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    [ProducesResponseType<PaginatedResponse<AdminWebhookDeliveryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListWebhookDeliveries(
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<AdminWebhookDeliveryDto>> result =
            await _adminService.GetWebhookDeliveryLogAsync(pagination, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>
    /// Replays one delivery: sends a brand-new attempt with the original's exact rendered body, appended as
    /// its own row (the original attempt is never mutated). Refused if the endpoint has since been deleted or
    /// disabled. Always audited, naming the acting operator.
    /// </summary>
    [HttpPost("webhooks/deliveries/{deliveryId:long}/replay")]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    [ProducesResponseType<StatusResponseDto<AdminWebhookReplayResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReplayWebhookDelivery(long deliveryId, CancellationToken ct)
    {
        if (!Guid.TryParse(_currentUser.UserId, out Guid actorUserId))
            return UnauthenticatedResponse();

        Result<AdminWebhookReplayResultDto> result = await _adminService.ReplayWebhookDeliveryAsync(
            deliveryId,
            actorUserId,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>
    /// The REAL background job queue (S-ADMIN-6b) — every <c>ScheduledPipelineTask</c> row across every
    /// tenant, newest first, paged. Never a fabricated queue: this is the exact primitive
    /// <c>ScheduledPipelineExpiryService</c> sweeps and dispatches.
    /// </summary>
    [HttpGet("jobs")]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    [ProducesResponseType<PaginatedResponse<AdminScheduledJobDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListScheduledJobs(
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<AdminScheduledJobDto>> result =
            await _adminService.GetScheduledJobQueueAsync(pagination, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>
    /// Retries one failed (expired) scheduled job: schedules a brand-new deferred run for the same pipeline,
    /// due immediately, appended as its own row — the original failed attempt is never mutated. Refused when
    /// the job already succeeded, is still queued, was cancelled, or its target pipeline no longer exists.
    /// Always audited, naming the acting operator.
    /// </summary>
    [HttpPost("jobs/{taskId:guid}/retry")]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    [EnableRateLimiting(SecuritySensitiveRateLimitPolicy.PolicyName)]
    [ProducesResponseType<StatusResponseDto<AdminScheduledJobRetryResultDto>>(
        StatusCodes.Status200OK
    )]
    public async Task<IActionResult> RetryScheduledJob(Guid taskId, CancellationToken ct)
    {
        if (!Guid.TryParse(_currentUser.UserId, out Guid actorUserId))
            return UnauthenticatedResponse();

        Result<AdminScheduledJobRetryResultDto> result = await _adminService.RetryScheduledJobAsync(
            taskId,
            actorUserId,
            ct
        );
        return ResultResponse(result);
    }

    /// <summary>
    /// Per-tenant usage for each tenant's most recent metering period (S-ADMIN-6b), computed purely from
    /// recorded usage rows — never a fabricated figure. The period each row covers is stated explicitly.
    /// </summary>
    [HttpGet("usage")]
    [Authorize(Policy = IamPermissionKeys.IamManage)]
    [ProducesResponseType<PaginatedResponse<AdminTenantUsageDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTenantUsage(
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<AdminTenantUsageDto>> result = await _adminService.GetTenantUsageAsync(
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }
}
