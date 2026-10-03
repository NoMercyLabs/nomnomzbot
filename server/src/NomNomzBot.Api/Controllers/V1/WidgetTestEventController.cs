// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Identifiers;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>Fire a sample overlay event so a streamer (or we) can PREVIEW a widget without waiting for a real
/// follow/sub/cheer — the "test this overlay" affordance. It routes a representative <c>WidgetEvent</c> through the
/// exact same dispatch a real event uses (<see cref="WidgetAlertDispatch"/>), so only the widgets that subscribe to
/// that event type react, exactly as they would live.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/channels/{channelId}/widgets")]
[Authorize]
[Tags("Widgets")]
public sealed class WidgetTestEventController : BaseController
{
    private readonly IApplicationDbContext _db;
    private readonly IWidgetNotifier _notifier;
    private readonly IOverlayPresenceRegistry _presence;
    private readonly TimeProvider _clock;

    public WidgetTestEventController(
        IApplicationDbContext db,
        IWidgetNotifier notifier,
        IOverlayPresenceRegistry presence,
        TimeProvider clock
    )
    {
        _db = db;
        _notifier = notifier;
        _presence = presence;
        _clock = clock;
    }

    /// <summary>The sample payload of every event type, keyed by type, plus the fallback under "_default". The
    /// editor preview fires from this table, so a preview test sends what <see cref="Fire"/> sends.</summary>
    [RequireAction("widget:read")]
    [HttpGet("test-event/samples")]
    [ProducesResponseType<StatusResponseDto<Dictionary<string, object>>>(StatusCodes.Status200OK)]
    public IActionResult Samples(string channelId) =>
        Ok(
            new StatusResponseDto<Dictionary<string, object>>
            {
                Data = WidgetTestSamples.Table(_clock.GetUtcNow()),
            }
        );

    /// <summary>Fire a sample event of <paramref name="request"/>.EventType to the channel's subscribed widgets.</summary>
    [RequireAction("widget:write")]
    [HttpPost("test-event")]
    [ProducesResponseType<StatusResponseDto<string>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Fire(
        string channelId,
        [FromBody] WidgetTestEventRequest request,
        CancellationToken ct
    )
    {
        // No null guard: [ApiController] on BaseController rejects a null or unbindable body with a 400 before
        // this runs, so the check was unreachable and only made the annotation look like it could lie.
        if (string.IsNullOrWhiteSpace(request.EventType))
            return BadRequestResponse("An eventType is required.");

        if (
            !GuidUlidCodec.TryDecode(channelId, out Guid broadcasterId)
            && !Guid.TryParse(channelId, out broadcasterId)
        )
            return BadRequestResponse("Invalid channel id.");

        // A custom payload arrives as JsonElement values (parsed from the body); normalize them to plain CLR so
        // the WidgetEvent serializes as real values, not {"ValueKind":...} — otherwise a widget driven by the
        // payload (chat box / poll / emote wall reading message + fragments) receives garbage and renders nothing.
        object data = request.Data is { } custom
            ? custom.ToDictionary(pair => pair.Key, pair => Normalize(pair.Value))
            : WidgetTestSamples.For(request.EventType, _clock.GetUtcNow());
        await WidgetAlertDispatch.RouteAsync(
            _db,
            _notifier,
            broadcasterId,
            request.EventType,
            data,
            // A manual test fire from the dashboard — no originating ChannelEvent row.
            excludeWidgetId: null,
            channelEventId: null,
            ct
        );
        return Ok(
            new StatusResponseDto<string>
            {
                Data = await DescribeReachAsync(broadcasterId, request.EventType, ct),
            }
        );
    }

    /// <summary>
    /// Says who could actually have received the test event. Reporting a bare "fired {eventType}" made a test
    /// that reached NOTHING look identical to one the stream heard: a channel with no subscribing widget, and a
    /// channel whose browser source is not open, both read as success — which is exactly how two identically
    /// configured channels appear to behave differently. The dispatch is the same either way; only the report
    /// changes, and it now names the reason nothing played.
    /// </summary>
    private async Task<string> DescribeReachAsync(
        Guid broadcasterId,
        string eventType,
        CancellationToken ct
    )
    {
        List<Widget> widgets = await _db
            .Widgets.AsNoTracking()
            .Where(w => w.BroadcasterId == broadcasterId)
            .ToListAsync(ct);
        List<Widget> subscribers = WidgetAlertRouting.Subscribers(widgets, eventType).ToList();
        if (subscribers.Count == 0)
            return $"fired {eventType} — no widget on this channel subscribes it";

        int attached = subscribers.Count(w => _presence.IsWidgetAttached(broadcasterId, w.Id));
        return attached == 0
            ? $"fired {eventType} to {subscribers.Count} widget(s) — none has a browser source open, so nothing played it"
            : $"fired {eventType} to {subscribers.Count} widget(s), {attached} with a browser source open";
    }

    /// <summary>Coerce a value (a <see cref="JsonElement"/> when it came from the request body) to a plain CLR
    /// object graph so it round-trips through the hub serializer as its value, not its reflected properties.</summary>
    private static object? Normalize(object? value)
    {
        if (value is not JsonElement element)
            return value;

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out long l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element
                .EnumerateArray()
                .Select(item => Normalize(item))
                .ToList(),
            JsonValueKind.Object => element
                .EnumerateObject()
                .ToDictionary(property => property.Name, property => Normalize(property.Value)),
            _ => null,
        };
    }
}

/// <summary>The test-fire request: an event type, and an optional explicit payload (a sample is used when omitted).</summary>
public sealed record WidgetTestEventRequest(string EventType, Dictionary<string, object?>? Data);
