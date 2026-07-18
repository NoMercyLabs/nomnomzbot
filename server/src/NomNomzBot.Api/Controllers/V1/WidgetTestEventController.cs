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
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Identifiers;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Persistence;

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

    public WidgetTestEventController(IApplicationDbContext db, IWidgetNotifier notifier)
    {
        _db = db;
        _notifier = notifier;
    }

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
        if (request is null || string.IsNullOrWhiteSpace(request.EventType))
            return BadRequestResponse("An eventType is required.");

        if (
            !GuidUlidCodec.TryDecode(channelId, out Guid broadcasterId)
            && !Guid.TryParse(channelId, out broadcasterId)
        )
            return BadRequestResponse("Invalid channel id.");

        object data = request.Data ?? WidgetTestSamples.For(request.EventType);
        await WidgetAlertDispatch.RouteAsync(
            _db,
            _notifier,
            broadcasterId,
            request.EventType,
            data,
            ct
        );
        return Ok(new StatusResponseDto<string> { Data = $"fired {request.EventType}" });
    }
}

/// <summary>The test-fire request: an event type, and an optional explicit payload (a sample is used when omitted).</summary>
public sealed record WidgetTestEventRequest(string EventType, Dictionary<string, object?>? Data);

/// <summary>Representative sample payloads per overlay event type, shaped like the real broadcast data so a preview
/// looks like the real thing.</summary>
internal static class WidgetTestSamples
{
    public static object For(string eventType) =>
        eventType switch
        {
            "follow" => new { user = "TestFollower" },
            "subscription" => new { user = "TestSubscriber", tier = "1000" },
            "resub" => new
            {
                user = "TestResubber",
                months = 6,
                tier = "1000",
            },
            "gift" => new
            {
                user = "TestGifter",
                amount = 5,
                tier = "1000",
            },
            "cheer" => new { user = "TestCheerer", amount = 500 },
            "raid" => new { user = "TestRaider", viewers = 42 },
            "ban" => new { user = "TestUser" },
            "now_playing" => new
            {
                isPlaying = true,
                track = "Test Track",
                artist = "Test Artist",
            },
            "hype_train_begin" or "hype_train_progress" => new
            {
                level = 2,
                progress = 350,
                goal = 1000,
            },
            "hype_train_end" => new { level = 3 },
            "ChatMessage" => new
            {
                userId = "test-chatter",
                username = "testchatter",
                displayName = "TestChatter",
                color = "#9146ff",
                message = "Hello from the test event! 1",
                fragments = new[] { new { type = "text", text = "Hello from the test event! 1" } },
                badges = Array.Empty<object>(),
                pronouns = "they/them",
            },
            _ => new { user = "TestUser" },
        };
}
