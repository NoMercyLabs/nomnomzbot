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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Marketplace.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The hosted-marketplace client surface (marketplace.md §5): browse the vetted catalog, install an item
/// through the ONE local import path (re-install = update), publish a bundle into the vetting queue, and
/// manage the channel's publisher token (write-only — the token is never echoed). Channel-routed like every
/// management surface; the marketplace itself is a separate NoMercy-hosted service the bot only calls.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/marketplace")]
[Authorize]
[Tags("Marketplace")]
public class MarketplaceController(
    IMarketplaceClient client,
    IMarketplaceService marketplace,
    IMarketplacePublisherTokenService publisherTokens,
    ICurrentUserService currentUser
) : BaseController
{
    // ── Browse ──────────────────────────────────────────────────────────────────

    /// <summary>Search the marketplace's approved listings (free text + type/tags filters), paginated.</summary>
    [HttpGet("items")]
    [RequireAction("bundles:read")]
    [ProducesResponseType<PaginatedResponse<MarketplaceItemDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchItems(
        string channelId,
        [FromQuery] PageRequestDto request,
        [FromQuery] string? type,
        [FromQuery] string? tags,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out _))
            return BadRequestResponse("Invalid channel id.");

        MarketplaceQuery query = new(
            request.Search,
            type,
            string.IsNullOrWhiteSpace(tags)
                ? null
                : tags.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
        );
        Result<PagedList<MarketplaceItemDto>> result = await client.SearchAsync(
            query,
            new PaginationParams(request.Page, request.Take),
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Read one marketplace listing.</summary>
    [HttpGet("items/{itemId}")]
    [RequireAction("bundles:read")]
    public async Task<IActionResult> GetItem(string channelId, string itemId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out _))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await client.GetItemAsync(itemId, ct));
    }

    // ── Install ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Download the item's bundle and install it under the given conflict policy (default: rename).
    /// Re-installing an already-installed item updates it in place instead of duplicating.
    /// </summary>
    [HttpPost("items/{itemId}/install")]
    [RequireAction("bundles:import")]
    public async Task<IActionResult> InstallItem(
        string channelId,
        string itemId,
        [FromBody] MarketplaceInstallRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();

        return ResultResponse(
            await marketplace.InstallAsync(broadcasterId, caller, itemId, request.Policy, ct)
        );
    }

    // ── Publish ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Publish a bundle ZIP into the marketplace's vetting queue. The ZIP is inspect-validated locally
    /// first — a bundle with issues is refused before any bytes are uploaded.
    /// </summary>
    [HttpPost("publish")]
    [RequireAction("bundles:publish")]
    public async Task<IActionResult> Publish(
        string channelId,
        IFormFile file,
        [FromForm] MarketplacePublishForm form,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (file.Length == 0)
            return BadRequestResponse("Upload a bundle ZIP file.");

        PublishMetadata metadata = new(
            form.Name,
            form.Version,
            form.Summary,
            string.IsNullOrWhiteSpace(form.Tags)
                ? null
                : form.Tags.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
        );
        await using Stream zip = file.OpenReadStream();
        return ResultResponse(await marketplace.PublishAsync(broadcasterId, zip, metadata, ct));
    }

    /// <summary>Poll a publish submission's vetting status (pending | approved | rejected + reason).</summary>
    [HttpGet("submissions/{submissionId}")]
    [RequireAction("bundles:publish")]
    public async Task<IActionResult> GetSubmission(
        string channelId,
        string submissionId,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await client.GetSubmissionAsync(broadcasterId, submissionId, ct));
    }

    // ── Publisher token (write-only custody) ────────────────────────────────────

    /// <summary>Whether a publisher token is stored. The token value itself is never readable.</summary>
    [HttpGet("publisher-token")]
    [RequireAction("bundles:publish")]
    public async Task<IActionResult> GetPublisherTokenStatus(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await publisherTokens.GetStatusAsync(broadcasterId, ct));
    }

    /// <summary>Store (or replace) the channel's marketplace publisher token. Vaulted; never echoed back.</summary>
    [HttpPut("publisher-token")]
    [RequireAction("bundles:publish")]
    public async Task<IActionResult> SetPublisherToken(
        string channelId,
        [FromBody] MarketplacePublisherTokenRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();

        return ResultResponse(
            await publisherTokens.SetTokenAsync(broadcasterId, request.Token, caller, ct)
        );
    }

    /// <summary>Clear the stored publisher token. Idempotent.</summary>
    [HttpDelete("publisher-token")]
    [RequireAction("bundles:publish")]
    public async Task<IActionResult> ClearPublisherToken(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await publisherTokens.ClearTokenAsync(broadcasterId, ct));
    }

    private bool TryGetCaller(out Guid caller) => Guid.TryParse(currentUser.UserId, out caller);
}

/// <summary>Install request body: what to do when a bundle item's name already exists on the channel.</summary>
public sealed record MarketplaceInstallRequest(
    ImportConflictPolicy Policy = ImportConflictPolicy.Rename
);

/// <summary>The publish form fields accompanying the multipart bundle ZIP.</summary>
public sealed class MarketplacePublishForm
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? Summary { get; set; }

    /// <summary>Comma-separated listing tags.</summary>
    public string? Tags { get; set; }
}

/// <summary>Write-only publisher-token body. The stored token is never echoed back by any endpoint.</summary>
public sealed record MarketplacePublisherTokenRequest(string Token);
