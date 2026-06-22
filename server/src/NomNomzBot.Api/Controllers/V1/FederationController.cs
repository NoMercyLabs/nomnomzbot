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
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Federation;
using NomNomzBot.Application.DTOs.Federation;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The global federation trust directory (federation-oidc.md §5) — peer registration, trust/revoke lifecycle, and
/// key rotation. Platform-operator scope (the Plane-C IAM <c>iam:manage</c>/<c>audit:read</c> policy gate is the
/// target — currently the live <c>admin</c> role gate). (Deferred: the mTLS handshake/inbound endpoints + the
/// public descriptor await the handshake transport.)
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/federation")]
[Authorize(Roles = "admin")]
[Tags("Federation")]
public class FederationController(IFederationPeerService peers, ICurrentUserService currentUser)
    : BaseController
{
    [HttpGet("peers")]
    [ProducesResponseType<PaginatedResponse<FederationPeerDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPeers(
        [FromQuery] PageRequestDto request,
        [FromQuery] string? trustState,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<FederationPeerDto>> result = await peers.ListPeersAsync(
            pagination,
            trustState,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    [HttpGet("peers/{peerId:guid}")]
    public async Task<IActionResult> GetPeer(Guid peerId, CancellationToken ct) =>
        ResultResponse(await peers.GetPeerAsync(peerId, ct));

    [HttpPost("peers")]
    public async Task<IActionResult> RegisterPeer(
        [FromBody] RegisterFederationPeerRequest request,
        CancellationToken ct
    ) => ResultResponse(await peers.RegisterPeerAsync(request, ct));

    [HttpPost("peers/{peerId:guid}/trust")]
    public async Task<IActionResult> TrustPeer(Guid peerId, CancellationToken ct)
    {
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(await peers.TrustPeerAsync(peerId, caller, ct));
    }

    [HttpPost("peers/{peerId:guid}/revoke")]
    public async Task<IActionResult> RevokePeer(
        Guid peerId,
        [FromBody] RevokeFederationPeerRequest request,
        CancellationToken ct
    )
    {
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(await peers.RevokePeerAsync(peerId, request, caller, ct));
    }

    [HttpPost("peers/{peerId:guid}/keys")]
    public async Task<IActionResult> AddPeerKey(
        Guid peerId,
        [FromBody] AddFederationPeerKeyRequest request,
        CancellationToken ct
    ) => ResultResponse(await peers.AddPeerKeyAsync(peerId, request, ct));

    [HttpDelete("peers/{peerId:guid}/keys/{keyId}")]
    public async Task<IActionResult> DeactivatePeerKey(
        Guid peerId,
        string keyId,
        CancellationToken ct
    ) => ResultResponse(await peers.DeactivatePeerKeyAsync(peerId, keyId, ct));

    private bool TryGetCaller(out Guid caller) => Guid.TryParse(currentUser.UserId, out caller);
}
