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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Api.Controllers.V1;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/permissions")]
[Authorize]
[Tags("Permissions")]
public class PermissionsController : BaseController
{
    private readonly IPermissionService _permissionService;
    private readonly IApplicationDbContext _db;

    public PermissionsController(IPermissionService permissionService, IApplicationDbContext db)
    {
        _permissionService = permissionService;
        _db = db;
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    public record PermissionDto(
        int Id,
        string SubjectType,
        string SubjectId,
        string? SubjectName,
        string ResourceType,
        string? ResourceId,
        string PermissionValue,
        DateTime CreatedAt
    );

    public record GrantPermissionRequest(string UserId, string Permission);

    // ── List permissions ─────────────────────────────────────────────────────

    [RequireAction("roles:read")]
    [HttpGet]
    [ProducesResponseType<StatusResponseDto<List<PermissionDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPermissions(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        List<Permission> permissions = await _db
            .Permissions.Where(p => p.BroadcasterId == broadcasterId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

        // Permission.SubjectId holds the Twitch user string id — join on User.TwitchUserId.
        List<string> userIds = permissions
            .Where(p => p.SubjectType == "user")
            .Select(p => p.SubjectId)
            .Distinct()
            .ToList();

        Dictionary<string, User> users = await _db
            .Users.Where(u => userIds.Contains(u.TwitchUserId))
            .ToDictionaryAsync(u => u.TwitchUserId, ct);

        List<PermissionDto> result = permissions
            .Select(p =>
            {
                string? subjectName = null;
                if (p.SubjectType == "user" && users.TryGetValue(p.SubjectId, out User? user))
                    subjectName = user.DisplayName;

                return new PermissionDto(
                    p.Id,
                    p.SubjectType,
                    p.SubjectId,
                    subjectName,
                    p.ResourceType,
                    p.ResourceId,
                    p.PermissionValue,
                    p.CreatedAt
                );
            })
            .ToList();

        return Ok(new StatusResponseDto<List<PermissionDto>> { Data = result });
    }

    // ── Grant permission ─────────────────────────────────────────────────────

    [RequireAction("roles:manage")]
    [HttpPost]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GrantPermission(
        string channelId,
        [FromBody] GrantPermissionRequest request,
        CancellationToken ct
    )
    {
        Result grantResult = await _permissionService.GrantAsync(
            channelId,
            request.UserId,
            request.Permission,
            ct
        );

        return ResultResponse(grantResult);
    }

    // ── Revoke permission ────────────────────────────────────────────────────

    [RequireAction("roles:manage")]
    [HttpDelete("{userId}")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokePermission(
        string channelId,
        string userId,
        [FromQuery] string permission,
        CancellationToken ct
    )
    {
        Result revokeResult = await _permissionService.RevokeAsync(
            channelId,
            userId,
            permission,
            ct
        );

        return ResultResponse(revokeResult);
    }
}
