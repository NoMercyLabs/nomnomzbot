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
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Models;
using NomNomzBot.Api.RateLimiting;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Consequences;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Marketplace.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Local bundle import/export (marketplace.md §5) — the zero-infra half of the marketplace: export a
/// channel's content as a portable ZIP, inspect/import a ZIP, and manage the installed-bundle ledger.
/// Channel-routed like every management surface, so an operator can act on any channel they moderate.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/bundles")]
[Authorize]
[Tags("Bundles")]
public class BundlesController(
    IBundleExportService export,
    IBundleImportService import,
    ICurrentUserService currentUser
) : BaseController
{
    /// <summary>Export the selected entities as a portable bundle ZIP (secrets stripped).</summary>
    [HttpPost("export")]
    [EnableRateLimiting(RateLimitPolicyNames.WriteExpensive)]
    [RequireAction("bundles:export")]
    [Produces("application/zip", "application/json")]
    public async Task<IActionResult> Export(
        string channelId,
        [FromBody] ExportRequest request,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");

        Result<Stream> result = await export.ExportAsync(broadcasterId, request, ct);
        if (result.IsFailure)
            return ResultResponse(result);

        string fileName = $"{SafeFileName(request.Metadata.Name)}.zip";
        return File(result.Value, "application/zip", fileName);
    }

    /// <summary>Inspect an uploaded bundle ZIP without installing: manifest, capability summary, issues.</summary>
    [HttpPost("inspect")]
    [EnableRateLimiting(RateLimitPolicyNames.WriteExpensive)]
    [RequireAction("bundles:import")]
    public async Task<IActionResult> Inspect(string channelId, IFormFile file, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (file.Length == 0)
            return BadRequestResponse("Upload a bundle ZIP file.");

        await using Stream zip = file.OpenReadStream();
        return ResultResponse(await import.InspectAsync(broadcasterId, zip, ct));
    }

    /// <summary>Install an uploaded bundle ZIP under the given conflict policy (default: rename).</summary>
    [HttpPost("import")]
    [EnableRateLimiting(RateLimitPolicyNames.WriteExpensive)]
    [RequireAction("bundles:import")]
    public async Task<IActionResult> Import(
        string channelId,
        IFormFile file,
        [FromQuery] ImportConflictPolicy policy = ImportConflictPolicy.Rename,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (file.Length == 0)
            return BadRequestResponse("Upload a bundle ZIP file.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();

        await using Stream zip = file.OpenReadStream();
        return ResultResponse(
            await import.ImportAsync(
                broadcasterId,
                caller,
                zip,
                policy,
                marketplaceItemId: null,
                ct
            )
        );
    }

    /// <summary>List the channel's installed bundles.</summary>
    [HttpGet("installed")]
    [RequireAction("bundles:read")]
    public async Task<IActionResult> ListInstalled(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await import.ListInstalledAsync(broadcasterId, ct));
    }

    /// <summary>
    /// Real, counted blast radius for uninstalling this bundle - what it removes, per kind, by name (S-CONSEQ). The dashboard MUST call this and render the
    /// result before the confirm can proceed.
    /// </summary>
    [DestructiveAction(HasCountedBlastRadius = true)]
    [HttpGet("installed/{id:guid}/blast-radius")]
    [RequireAction("bundles:read")]
    [ProducesResponseType<StatusResponseDto<BlastRadiusDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUninstallBlastRadius(
        string channelId,
        Guid id,
        CancellationToken ct
    )
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        return ResultResponse(await import.GetUninstallBlastRadiusAsync(broadcasterId, id, ct));
    }

    /// <summary>Uninstall a bundle: removes exactly the entities it installed, then retires the ledger row.
    /// The confirm step calls <see cref="GetUninstallBlastRadius"/> first and shows what goes.</summary>
    [DestructiveAction(HasCountedBlastRadius = true)]
    [HttpDelete("installed/{id:guid}")]
    [RequireAction("bundles:import")]
    public async Task<IActionResult> Uninstall(string channelId, Guid id, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid broadcasterId))
            return BadRequestResponse("Invalid channel id.");
        if (!TryGetCaller(out Guid caller))
            return UnauthenticatedResponse();
        return ResultResponse(await import.UninstallAsync(broadcasterId, id, caller, ct));
    }

    private bool TryGetCaller(out Guid caller) => Guid.TryParse(currentUser.UserId, out caller);

    private static string SafeFileName(string name)
    {
        char[] safe =
        [
            .. name.Trim().Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-'),
        ];
        string fileName = new(safe);
        return fileName.Length == 0 ? "bundle" : fileName;
    }
}
