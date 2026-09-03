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

    public AdminController(
        IAdminService adminService,
        IApplicationDbContext db,
        IDekRotationService dekRotationService,
        IProviderCredentialService providerCredentials
    )
    {
        _adminService = adminService;
        _db = db;
        _dekRotationService = dekRotationService;
        _providerCredentials = providerCredentials;
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
    [DestructiveAction]
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
}
