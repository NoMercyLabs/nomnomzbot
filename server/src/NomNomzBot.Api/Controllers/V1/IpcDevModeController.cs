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
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Ipc;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// IPC dev-mode key registry (stream-admin.md §5.3) — manages the keys that gate the local-IPC
/// developer hook-in. The socket itself is process-local (Unix domain socket / named pipe), never
/// HTTP; only the registry lives here.
/// <para>
/// Owner gate: the Plane-C policy <c>system:ipc:manage</c> — the handler first demands the platform
/// principal claim (minted only for <c>User.IsPlatformPrincipal</c>, which on self-host is
/// bootstrapped for the instance owner), and on self-host <c>AuthorizePlatformAsync</c>
/// short-circuits to allow, so the owner needs no IAM rows. On SaaS every route additionally
/// short-circuits to <c>503 ServiceUnavailable</c> before touching the registry — IPC dev mode is a
/// self-host feature.
/// </para>
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/system/ipc")]
[Authorize(Policy = IamPermissionKeys.SystemIpcManage)]
[Tags("System")]
public class IpcDevModeController(
    IIpcDevModeService ipc,
    IDeploymentProfileService profile,
    NomNomzBot.Application.Abstractions.Auth.ICurrentUserService currentUser
) : BaseController
{
    private const string SaasRefusal = "IPC dev mode is a self-host feature.";

    private bool IsSaas => profile.Current.Mode == DeploymentMode.Saas;

    /// <summary>Whether IPC dev mode is currently enabled (non-SaaS profile + at least one live key).</summary>
    [HttpGet("")]
    [ProducesResponseType<StatusResponseDto<bool>>(StatusCodes.Status200OK)]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetEnabled(CancellationToken ct)
    {
        if (IsSaas)
            return ServiceUnavailableResponse(SaasRefusal);
        return ResultResponse(await ipc.IsEnabledAsync(ct));
    }

    /// <summary>List the registered IPC keys — metadata only, never key material.</summary>
    [HttpGet("keys")]
    [ProducesResponseType<StatusResponseDto<List<IpcDevModeKeyDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ListKeys(CancellationToken ct)
    {
        if (IsSaas)
            return ServiceUnavailableResponse(SaasRefusal);
        return ResultResponse(await ipc.ListKeysAsync(ct));
    }

    /// <summary>Create an IPC key; the response carries the plaintext exactly once.</summary>
    [HttpPost("keys")]
    [ProducesResponseType<StatusResponseDto<IpcDevModeKeyDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> CreateKey(
        [FromBody] CreateIpcKeyRequest request,
        CancellationToken ct
    )
    {
        if (IsSaas)
            return ServiceUnavailableResponse(SaasRefusal);
        if (!Guid.TryParse(currentUser.UserId, out Guid caller))
            return UnauthenticatedResponse();

        Result<IpcDevModeKeyDto> result = await ipc.CreateKeyAsync(caller, request, ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return StatusCode(
            StatusCodes.Status201Created,
            new StatusResponseDto<IpcDevModeKeyDto> { Data = result.Value }
        );
    }

    /// <summary>Revoke an IPC key (soft-delete tombstone — the row stays for the audit trail).</summary>
    [HttpDelete("keys/{keyId:guid}")]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status200OK)]
    [ProducesResponseType<StatusResponseDto<object>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> RevokeKey(Guid keyId, CancellationToken ct)
    {
        if (IsSaas)
            return ServiceUnavailableResponse(SaasRefusal);
        return ResultResponse(await ipc.RevokeKeyAsync(keyId, ct));
    }
}
