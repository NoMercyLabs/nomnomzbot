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
using NomNomzBot.Api.Identifiers;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.DevPlatform;
using NomNomzBot.Application.DevPlatform.Dtos;
using NomNomzBot.Application.DevPlatform.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>
/// The developer-platform SDK surface (dev-platform.md §1.3, §8) — serves the reflection-generated TypeScript
/// types and the event catalog straight from the running server, so an editor's types always match the live
/// event set. Both endpoints are Gate-2 gated on <c>sdk:read</c> (any authenticated caller on their own
/// channel); the <c>?context=</c> selector chooses the visibility tier set (<c>widget</c> = Public only,
/// <c>script</c> = up to Broadcaster).
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/sdk")]
[Authorize]
[Tags("Developer SDK")]
public class SdkController : BaseController
{
    private readonly ISdkTypeEmitter _emitter;
    private readonly ICodeScriptTriggerResolver _triggers;
    private readonly IWidgetService _widgets;
    private readonly ICurrentTenantService _currentTenant;

    public SdkController(
        ISdkTypeEmitter emitter,
        ICodeScriptTriggerResolver triggers,
        IWidgetService widgets,
        ICurrentTenantService currentTenant
    )
    {
        _emitter = emitter;
        _triggers = triggers;
        _widgets = widgets;
        _currentTenant = currentTenant;
    }

    /// <summary>
    /// The generated <c>nnz.d.ts</c> for the requested context, as <c>text/plain</c> — the <c>NnzEventMap</c>,
    /// every payload interface, and the typed <c>nnz.on&lt;K&gt;</c> declaration. With <c>trigger</c> (an event
    /// response key or <c>command</c>, script context only) <c>bot.getVar</c> is typed to that trigger's variable
    /// keys. With <c>widget</c> (a widget id in the form the widget routes use, widget context only) the widget's
    /// own settings are typed from its settings schema; a widget of another channel is a <c>404</c>. <c>400</c> on
    /// an unknown context or trigger.
    /// </summary>
    [HttpGet("types.d.ts")]
    [RequireAction("sdk:read")]
    [Produces("text/plain")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTypes(
        [FromQuery] string? context,
        [FromQuery] string? trigger,
        [FromQuery] Guid? script,
        [FromQuery] string? widget = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!TryParseContext(context, out SdkContext parsed))
            return BadRequestResponse("Unknown context — use 'widget' or 'script'.");

        if (!string.IsNullOrWhiteSpace(widget))
            return await GetWidgetTypesAsync(parsed, trigger, script, widget, cancellationToken);

        bool hasTrigger = !string.IsNullOrWhiteSpace(trigger);
        if (!hasTrigger && script is null)
            return Content(_emitter.EmitTypeScript(parsed), "text/plain");

        if (parsed != SdkContext.Script)
            return BadRequestResponse("A trigger or script applies to the script context only.");

        if (hasTrigger && script is not null)
            return BadRequestResponse("Pass either a trigger or a script, not both.");

        if (script is { } scriptId)
            return await GetScriptTypesAsync(scriptId, cancellationToken);

        Result<string> typed = _emitter.EmitTypeScript(parsed, trigger!);
        return typed.IsSuccess
            ? Content(typed.Value, "text/plain")
            : BadRequestResponse(typed.ErrorMessage, typed.ErrorCode);
    }

    // A widget's settings come from its schema, read for the caller's own channel only. A widget with no schema
    // gets the plain untyped surface — never a narrow type that could flag a key the widget really reads.
    private async Task<IActionResult> GetWidgetTypesAsync(
        SdkContext parsed,
        string? trigger,
        Guid? script,
        string widget,
        CancellationToken cancellationToken
    )
    {
        if (
            parsed != SdkContext.Widget
            || !string.IsNullOrWhiteSpace(trigger)
            || script is not null
        )
            return BadRequestResponse("A widget applies to the widget context only.");

        if (_currentTenant.BroadcasterId is not { } broadcasterId)
            return NotFoundResponse("Widget not found.", "NOT_FOUND");

        string widgetId = GuidUlidCodec.TryDecode(widget, out Guid decoded)
            ? decoded.ToString()
            : widget;
        Result<WidgetSettingsSchema> schema = await _widgets.GetSettingsSchemaAsync(
            broadcasterId.ToString(),
            widgetId,
            cancellationToken
        );
        if (schema.IsSuccess)
            return Content(_emitter.EmitWidgetTypeScript(schema.Value), "text/plain");

        return schema.ErrorCode switch
        {
            "WIDGET_NO_SETTINGS_SCHEMA" or "WIDGET_SETTINGS_INVALID" => Content(
                _emitter.EmitTypeScript(SdkContext.Widget),
                "text/plain"
            ),
            _ => ResultResponse(schema),
        };
    }

    // A script with no known trigger, or one whose trigger cannot list its variables, gets the plain untyped
    // surface — never a narrow type that could flag a key a real trigger sets.
    private async Task<IActionResult> GetScriptTypesAsync(
        Guid scriptId,
        CancellationToken cancellationToken
    )
    {
        Result<IReadOnlyList<string>> triggers = await _triggers.GetTriggerKeysAsync(
            scriptId,
            cancellationToken
        );
        if (!triggers.IsSuccess)
            return NotFoundResponse(triggers.ErrorMessage, triggers.ErrorCode);

        if (triggers.Value.Count > 0)
        {
            Result<string> typed = _emitter.EmitTypeScript(SdkContext.Script, triggers.Value);
            if (typed.IsSuccess)
                return Content(typed.Value, "text/plain");
        }

        return Content(_emitter.EmitTypeScript(SdkContext.Script), "text/plain");
    }

    /// <summary>
    /// The event catalog for the requested context — one item per visible event: stable wire name, visibility
    /// tier, and the payload JSON Schema. <c>400</c> on an unknown context.
    /// </summary>
    [HttpGet("event-catalog")]
    [RequireAction("sdk:read")]
    [ProducesResponseType<StatusResponseDto<IReadOnlyList<EventCatalogItemDto>>>(
        StatusCodes.Status200OK
    )]
    public IActionResult GetEventCatalog([FromQuery] string? context)
    {
        if (!TryParseContext(context, out SdkContext parsed))
            return BadRequestResponse("Unknown context — use 'widget' or 'script'.");

        return Ok(
            new StatusResponseDto<IReadOnlyList<EventCatalogItemDto>>
            {
                Data = _emitter.EmitEventCatalog(parsed),
            }
        );
    }

    // Missing context defaults to the fuller 'script' set; a present-but-unrecognised value is a 400.
    private static bool TryParseContext(string? context, out SdkContext parsed)
    {
        if (
            string.IsNullOrWhiteSpace(context)
            || context.Equals("script", StringComparison.OrdinalIgnoreCase)
        )
        {
            parsed = SdkContext.Script;
            return true;
        }
        if (context.Equals("widget", StringComparison.OrdinalIgnoreCase))
        {
            parsed = SdkContext.Widget;
            return true;
        }
        parsed = SdkContext.Script;
        return false;
    }
}
