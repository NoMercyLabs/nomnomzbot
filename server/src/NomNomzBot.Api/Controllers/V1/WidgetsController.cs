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
using Microsoft.Extensions.Configuration;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Extensions;
using NomNomzBot.Api.Identifiers;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// Manages a channel's OBS browser-source overlay widgets (alerts, now-playing, and other overlay instances).
/// Routes are scoped to <c>{channelId}</c> and require authorization.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/widgets")]
[Authorize]
[Tags("Widgets")]
public class WidgetsController : BaseController
{
    private readonly IWidgetService _widgetService;
    private readonly IConfiguration _configuration;

    public WidgetsController(IWidgetService widgetService, IConfiguration configuration)
    {
        _widgetService = widgetService;
        _configuration = configuration;
    }

    /// <summary>
    /// Decode a widget/version/gallery route id from its wire form to the canonical guid the services parse. Owned
    /// ids are serialized to clients as 26-char ULID strings (<see cref="UlidGuidJsonConverter"/>), so
    /// <c>WidgetDetail.id</c> et al. come back as ULIDs; these route params are typed <c>string</c> and reach the
    /// service via <c>Guid.TryParse</c>, which rejects a ULID. Normalizing here (accepting ULID OR raw guid) is what
    /// makes the editor's compile/versions/rollback calls resolve instead of 404-ing. An undecodable value is passed
    /// through so the service returns its own NOT_FOUND.
    /// </summary>
    private static string Decode(string id) =>
        GuidUlidCodec.TryDecode(id, out Guid guid) ? guid.ToString() : id;

    /// <summary>
    /// Rewrites the OBS browser-source URL to the origin the operator actually reached the dashboard on, so a
    /// copied URL works in OBS instead of pointing at the bot's loopback bind (the bug where every URL read
    /// <c>http://localhost:8080</c>). The overlay host page is served by THIS API, so its public origin is the
    /// forwarded access origin — resolved identically to every OAuth <c>redirect_uri</c>
    /// (<see cref="PublicOriginExtensions.ResolvePublicOrigin"/>). An operator who deliberately fronts overlays on
    /// a different host still wins via an explicit <c>OverlayBaseUrl</c>. The widget's path + token query are kept
    /// verbatim; only the scheme+host are swapped.
    /// </summary>
    private WidgetDetail WithOverlayOrigin(WidgetDetail widget)
    {
        if (
            widget.OverlayUrl is null
            || !Uri.TryCreate(widget.OverlayUrl, UriKind.Absolute, out Uri? current)
        )
            return widget;

        string? explicitBase = _configuration["OverlayBaseUrl"];
        string origin = string.IsNullOrWhiteSpace(explicitBase)
            ? Request.ResolvePublicOrigin(_configuration)
            : explicitBase.TrimEnd('/');

        return widget with
        {
            OverlayUrl = $"{origin}{current.PathAndQuery}",
        };
    }

    /// <summary>List a channel's overlay widgets, paginated.</summary>
    [RequireAction("widget:read")]
    [HttpGet]
    [ProducesResponseType<PaginatedResponse<WidgetDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListWidgets(
        string channelId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<WidgetDetail>> result = await _widgetService.ListAsync(
            channelId,
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);

        PagedList<WidgetDetail> page = result.Value;
        PagedList<WidgetDetail> withOrigin = new(
            page.Items.Select(WithOverlayOrigin).ToList(),
            page.Page,
            page.PageSize,
            page.TotalCount
        );
        return GetPaginatedResponse(withOrigin, request);
    }

    /// <summary>List the starter templates offered when creating a new custom widget.</summary>
    [RequireAction("widget:read")]
    [HttpGet("templates")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<WidgetTemplate>>>(
        StatusCodes.Status200OK
    )]
    public IActionResult ListWidgetTemplates(string channelId)
    {
        IReadOnlyList<WidgetTemplate> templates = _widgetService.GetTemplates();
        return Ok(new StatusResponseDto<IReadOnlyList<WidgetTemplate>> { Data = templates });
    }

    /// <summary>Get a single overlay widget's configuration.</summary>
    [RequireAction("widget:read")]
    [HttpGet("{widgetId}")]
    [ProducesResponseType<StatusResponseDto<WidgetDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWidget(
        string channelId,
        string widgetId,
        CancellationToken ct
    )
    {
        Result<WidgetDetail> result = await _widgetService.GetAsync(
            channelId,
            Decode(widgetId),
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<WidgetDetail> { Data = WithOverlayOrigin(result.Value) });
    }

    /// <summary>Create a new overlay widget for a channel.</summary>
    [RequireAction("widget:write")]
    [HttpPost]
    [ProducesResponseType<StatusResponseDto<WidgetDetail>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateWidget(
        string channelId,
        [FromBody] CreateWidgetRequest request,
        CancellationToken ct
    )
    {
        Result<WidgetDetail> result = await _widgetService.CreateAsync(channelId, request, ct);
        if (result.IsFailure)
            return ResultResponse(result);

        return CreatedAtAction(
            nameof(GetWidget),
            new { channelId, widgetId = result.Value.Id },
            new StatusResponseDto<WidgetDetail>
            {
                Data = WithOverlayOrigin(result.Value),
                Message = "Widget created successfully.",
            }
        );
    }

    /// <summary>Clone a widget into a new, fully-owned custom widget (source copied in + compiled, so it is live).</summary>
    [RequireAction("widget:write")]
    [HttpPost("clone")]
    [ProducesResponseType<StatusResponseDto<WidgetDetail>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CloneWidget(
        string channelId,
        [FromBody] CloneWidgetRequest request,
        CancellationToken ct
    )
    {
        Result<WidgetDetail> result = await _widgetService.CloneToEditAsync(channelId, request, ct);
        if (result.IsFailure)
            return ResultResponse(result);

        return CreatedAtAction(
            nameof(GetWidget),
            new { channelId, widgetId = result.Value.Id },
            new StatusResponseDto<WidgetDetail>
            {
                Data = WithOverlayOrigin(result.Value),
                Message = "Widget cloned successfully.",
            }
        );
    }

    /// <summary>Install a verified gallery widget into this channel (its source compiled into v1, immediately live).</summary>
    [RequireAction("widget:install")]
    [HttpPost("install/{galleryItemId}")]
    [ProducesResponseType<StatusResponseDto<WidgetDetail>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> InstallWidget(
        string channelId,
        string galleryItemId,
        CancellationToken ct
    )
    {
        Result<WidgetDetail> result = await _widgetService.InstallFromGalleryAsync(
            channelId,
            Decode(galleryItemId),
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);

        return CreatedAtAction(
            nameof(GetWidget),
            new { channelId, widgetId = result.Value.Id },
            new StatusResponseDto<WidgetDetail>
            {
                Data = WithOverlayOrigin(result.Value),
                Message = "Widget installed successfully.",
            }
        );
    }

    /// <summary>Update an existing overlay widget's configuration.</summary>
    [RequireAction("widget:write")]
    [HttpPut("{widgetId}")]
    [ProducesResponseType<StatusResponseDto<WidgetDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateWidget(
        string channelId,
        string widgetId,
        [FromBody] UpdateWidgetRequest request,
        CancellationToken ct
    )
    {
        Result<WidgetDetail> result = await _widgetService.UpdateAsync(
            channelId,
            Decode(widgetId),
            request,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<WidgetDetail> { Data = WithOverlayOrigin(result.Value) });
    }

    /// <summary>Delete an overlay widget from a channel.</summary>
    [RequireAction("widget:write")]
    [HttpDelete("{widgetId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteWidget(
        string channelId,
        string widgetId,
        CancellationToken ct
    )
    {
        Result result = await _widgetService.DeleteAsync(channelId, Decode(widgetId), ct);
        if (result.IsFailure)
            return ResultResponse(result);
        return NoContent();
    }

    /// <summary>
    /// Compile-on-save: append the widget's next version, build it, and (on success) activate it. A failed build
    /// is a persisted <c>error</c> version — the response carries the build status either way.
    /// </summary>
    [RequireAction("widget:compile")]
    [HttpPost("{widgetId}/compile")]
    [ProducesResponseType<StatusResponseDto<WidgetVersionDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CompileWidget(
        string channelId,
        string widgetId,
        [FromBody] CompileWidgetRequest request,
        CancellationToken ct
    )
    {
        Result<WidgetVersionDetail> result = await _widgetService.CompileAsync(
            channelId,
            Decode(widgetId),
            request,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<WidgetVersionDetail> { Data = result.Value });
    }

    /// <summary>List a widget's build/version history, newest first.</summary>
    [RequireAction("widget:version:read")]
    [HttpGet("{widgetId}/versions")]
    [ProducesResponseType<PaginatedResponse<WidgetVersionSummary>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListWidgetVersions(
        string channelId,
        string widgetId,
        [FromQuery] PageRequestDto request,
        CancellationToken ct
    )
    {
        PaginationParams pagination = new(request.Page, request.Take, request.Sort, request.Order);
        Result<PagedList<WidgetVersionSummary>> result = await _widgetService.ListVersionsAsync(
            channelId,
            Decode(widgetId),
            pagination,
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return GetPaginatedResponse(result.Value, request);
    }

    /// <summary>Get a single widget version in full (source + build log), for rollback/debug.</summary>
    [RequireAction("widget:version:read")]
    [HttpGet("{widgetId}/versions/{versionId}")]
    [ProducesResponseType<StatusResponseDto<WidgetVersionDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetWidgetVersion(
        string channelId,
        string widgetId,
        string versionId,
        CancellationToken ct
    )
    {
        Result<WidgetVersionDetail> result = await _widgetService.GetVersionAsync(
            channelId,
            Decode(widgetId),
            Decode(versionId),
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<WidgetVersionDetail> { Data = result.Value });
    }

    /// <summary>Roll the widget's active version back to an earlier successful build (no recompile).</summary>
    [RequireAction("widget:rollback")]
    [HttpPost("{widgetId}/rollback/{versionId}")]
    [ProducesResponseType<StatusResponseDto<WidgetDetail>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RollbackWidget(
        string channelId,
        string widgetId,
        string versionId,
        CancellationToken ct
    )
    {
        Result<WidgetDetail> result = await _widgetService.RollbackAsync(
            channelId,
            Decode(widgetId),
            Decode(versionId),
            ct
        );
        if (result.IsFailure)
            return ResultResponse(result);
        return Ok(new StatusResponseDto<WidgetDetail> { Data = WithOverlayOrigin(result.Value) });
    }
}
