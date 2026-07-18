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

        // A custom payload arrives as JsonElement values (parsed from the body); normalize them to plain CLR so
        // the WidgetEvent serializes as real values, not {"ValueKind":...} — otherwise a widget driven by the
        // payload (chat box / poll / emote wall reading message + fragments) receives garbage and renders nothing.
        object data = request.Data is { } custom
            ? custom.ToDictionary(pair => pair.Key, pair => Normalize(pair.Value))
            : WidgetTestSamples.For(request.EventType);
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

/// <summary>Representative sample payloads per overlay event type, shaped like the real broadcast data (the same
/// fields the <c>Hubs/Broadcasters/*</c> handlers push) so a preview renders exactly like the live event. Covers
/// every event type the shipped first-party widgets subscribe to, so each widget can be demonstrated from the
/// dashboard's "test this overlay" action without waiting for a real follow / poll / redemption.</summary>
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
            // Supporter events (supporter-events.md) — tip/charity carry a money amount + currency; membership/merch
            // are presence-only (the ticker / alerts widgets read `user` plus, for money, `amount`/`currency`).
            "supporter.tip" => new
            {
                user = "TestTipper",
                amount = 25,
                currency = "USD",
            },
            "supporter.membership" => new { user = "TestMember" },
            "supporter.merch" => new { user = "TestBuyer" },
            "supporter.charity" => new
            {
                user = "TestDonor",
                amount = 50,
                currency = "USD",
            },
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
            // Goal bar — a `goal` event is the authoritative value+target for its metric (goal_bar defaults to
            // "followers"), between which matching count events live-increment.
            "goal" => new
            {
                metric = "followers",
                value = 72,
                target = 100,
            },
            // Song-request queue snapshot (music-sr.md) — { items: [{ title, requestedBy, durationSec }] }.
            "sr_queue" => new
            {
                items = new[]
                {
                    new
                    {
                        title = "Never Gonna Give You Up",
                        requestedBy = "TestViewer",
                        durationSec = 213,
                    },
                    new
                    {
                        title = "Sandstorm",
                        requestedBy = "AnotherViewer",
                        durationSec = 225,
                    },
                    new
                    {
                        title = "Bohemian Rhapsody",
                        requestedBy = "ThirdViewer",
                        durationSec = 355,
                    },
                },
            },
            // Dispatched TTS caption (tts.md) — { text, voice, user, durationMs }.
            "tts_speak" => new
            {
                text = "Hey streamer, thanks for the awesome content!",
                voice = "en-US-JennyNeural",
                user = "TestViewer",
                durationMs = 3200,
            },
            // Poll bars (PollBeganAlertDto) — choices with vote + channel-point-vote tallies; the end frame names the
            // winning choice so the bar highlights.
            "poll_begin" or "poll_progress" => new
            {
                pollId = "test-poll",
                title = "What game next?",
                choices = new[]
                {
                    new
                    {
                        id = "c1",
                        title = "Elden Ring",
                        votes = 42,
                        channelPointsVotes = 10,
                    },
                    new
                    {
                        id = "c2",
                        title = "Minecraft",
                        votes = 28,
                        channelPointsVotes = 5,
                    },
                    new
                    {
                        id = "c3",
                        title = "Just Chatting",
                        votes = 15,
                        channelPointsVotes = 0,
                    },
                },
            },
            "poll_end" => new
            {
                pollId = "test-poll",
                title = "What game next?",
                winningChoiceId = "c1",
                choices = new[]
                {
                    new
                    {
                        id = "c1",
                        title = "Elden Ring",
                        votes = 62,
                        channelPointsVotes = 18,
                    },
                    new
                    {
                        id = "c2",
                        title = "Minecraft",
                        votes = 28,
                        channelPointsVotes = 5,
                    },
                    new
                    {
                        id = "c3",
                        title = "Just Chatting",
                        votes = 15,
                        channelPointsVotes = 0,
                    },
                },
            },
            // Prediction bars (PredictionBeganAlertDto) — outcomes with channel-point totals; end names the winner.
            "prediction_begin" or "prediction_progress" or "prediction_lock" => new
            {
                predictionId = "test-pred",
                title = "Will we beat the boss this attempt?",
                outcomes = new[]
                {
                    new
                    {
                        id = "o1",
                        title = "Yes",
                        channelPoints = 12000,
                        users = 42,
                        color = "BLUE",
                    },
                    new
                    {
                        id = "o2",
                        title = "No",
                        channelPoints = 4500,
                        users = 18,
                        color = "PINK",
                    },
                },
            },
            "prediction_end" => new
            {
                predictionId = "test-pred",
                title = "Will we beat the boss this attempt?",
                winningOutcomeId = "o1",
                outcomes = new[]
                {
                    new
                    {
                        id = "o1",
                        title = "Yes",
                        channelPoints = 12000,
                        users = 42,
                        color = "BLUE",
                    },
                    new
                    {
                        id = "o2",
                        title = "No",
                        channelPoints = 4500,
                        users = 18,
                        color = "PINK",
                    },
                },
            },
            // Channel-point redemption (RewardRedeemedDto) — redeemer, reward, cost, and their typed input.
            "reward_redeemed" => new
            {
                rewardId = "test-reward",
                rewardTitle = "Hydrate!",
                userDisplayName = "TestRedeemer",
                cost = 500,
                userInput = "Drink some water please!",
            },
            // Custom data source (custom-events.md) — { fields: { name: value } }; the sample binds the documented
            // heart-rate example the custom_data widget defaults to (source "heartrate", field "bpm").
            "custom.heartrate" => new { fields = new { bpm = 142 } },
            // Live-game frames share a `kind`-discriminated envelope across all four game widgets: `round_open` opens
            // the round, `results` (with a `results` roster) resolves it. The results item carries the superset each
            // game's board reads (player/won/payout plus drop's landed/distance and crash's multiplier).
            "game.lobby" or "game.running" => new
            {
                kind = "round_open",
                target = 50,
                radius = 12,
            },
            "game.resolved" => new
            {
                kind = "results",
                target = 50,
                winner = "TestWinner",
                results = new[]
                {
                    new
                    {
                        player = "TestWinner",
                        won = true,
                        payout = 500,
                        landed = 50,
                        distance = 0,
                        multiplier = 2.5,
                    },
                    new
                    {
                        player = "SecondPlace",
                        won = false,
                        payout = 0,
                        landed = 34,
                        distance = 16,
                        multiplier = 0.0,
                    },
                },
            },
            // Decorated chat message (ChatMessageBroadcastHandler) — fragments with resolved emote image urls, a
            // broadcaster badge, name colour, and pronouns; drives the chat box and populates the emote wall.
            "ChatMessage" => new
            {
                userId = "test-chatter",
                username = "testchatter",
                displayName = "TestChatter",
                color = "#9146ff",
                message = "Hey chat! Kappa this stream is amazing LUL 4Head",
                fragments = new object[]
                {
                    new { type = "text", text = "Hey chat! " },
                    Emote("Kappa", "25"),
                    new { type = "text", text = " this stream is amazing " },
                    Emote("LUL", "425618"),
                    new { type = "text", text = " " },
                    Emote("4Head", "354"),
                },
                badges = new object[]
                {
                    new
                    {
                        setId = "broadcaster",
                        urls = new Dictionary<string, string>
                        {
                            ["1"] =
                                "https://static-cdn.jtvnw.net/badges/v1/5527c58c-fb7d-422d-b71b-f309dcb85cc1/1",
                            ["2"] =
                                "https://static-cdn.jtvnw.net/badges/v1/5527c58c-fb7d-422d-b71b-f309dcb85cc1/2",
                            ["4"] =
                                "https://static-cdn.jtvnw.net/badges/v1/5527c58c-fb7d-422d-b71b-f309dcb85cc1/3",
                        },
                    },
                },
                pronouns = "they/them",
            },
            _ => new { user = "TestUser" },
        };

    // A resolved Twitch emote fragment — the shape the chat box and emote wall read (fr.emote.urls keyed by pixel
    // scale). The v2 emote CDN serves 1.0/2.0/3.0 renditions of a stable global emote id.
    private static object Emote(string name, string id) =>
        new
        {
            type = "emote",
            text = name,
            emote = new
            {
                id,
                provider = "twitch",
                urls = new Dictionary<string, string>
                {
                    ["1"] = $"https://static-cdn.jtvnw.net/emoticons/v2/{id}/default/dark/1.0",
                    ["2"] = $"https://static-cdn.jtvnw.net/emoticons/v2/{id}/default/dark/2.0",
                    ["3"] = $"https://static-cdn.jtvnw.net/emoticons/v2/{id}/default/dark/3.0",
                },
            },
        };
}
