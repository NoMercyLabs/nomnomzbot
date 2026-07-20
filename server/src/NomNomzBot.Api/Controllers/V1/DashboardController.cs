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
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Controllers.V1;

/// <summary>Aggregated per-channel stats and activity feed for the dashboard home screen.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/dashboard")]
[Authorize]
[Tags("Dashboard")]
public class DashboardController : BaseController
{
    private readonly IChannelRegistry _registry;
    private readonly IChannelService _channelService;
    private readonly IApplicationDbContext _db;
    private readonly ITwitchChannelsApi _channels;
    private readonly ITwitchSubscriptionsApi _subscriptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(
        IChannelRegistry registry,
        IChannelService channelService,
        IApplicationDbContext db,
        ITwitchChannelsApi channels,
        ITwitchSubscriptionsApi subscriptions,
        TimeProvider timeProvider,
        ILogger<DashboardController> logger
    )
    {
        _registry = registry;
        _channelService = channelService;
        _db = db;
        _channels = channels;
        _subscriptions = subscriptions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    public record ActivityEventDto(
        string Id,
        string Type,
        string? UserId,
        string? Username,
        string? Data,
        DateTime Timestamp
    );

    /// <summary>
    /// Returns a live stats snapshot for the given channel.
    /// Uses the in-memory ChannelContext when the bot is connected; falls back to DB otherwise.
    /// </summary>
    [HttpGet("{channelId}/stats")]
    [RequireAction("dashboard:read")]
    [ProducesResponseType<StatusResponseDto<DashboardStatsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStats(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        // Fetch follower count and channel info sequentially — both calls resolve the Twitch channel
        // ID via TwitchIdentityResolver which uses the scoped DbContext. Running them in parallel via
        // Task.WhenAll causes "A second operation was started on this context instance" because EF Core
        // DbContext is not thread-safe. Sequential execution is safe and still fast (<500 ms each).
        int followerCount = 0;
        int subscriberCount = 0;
        string? twitchTitle = null;
        string? twitchGame = null;

        try
        {
            Result<int> followerResult = await _channels.GetChannelFollowerCountAsync(tenantId, ct);
            followerCount = followerResult.IsSuccess ? followerResult.Value : 0;
            if (followerResult.IsFailure)
                _logger.LogWarning(
                    "Dashboard stats: follower count failed for {BroadcasterId}: {Error} ({Code}) — reporting 0",
                    tenantId,
                    followerResult.ErrorMessage,
                    followerResult.ErrorCode
                );

            Result<int> subscriberResult = await _subscriptions.GetSubscriberCountAsync(
                tenantId,
                ct
            );
            subscriberCount = subscriberResult.IsSuccess ? subscriberResult.Value : 0;
            if (subscriberResult.IsFailure)
                _logger.LogWarning(
                    "Dashboard stats: subscriber count failed for {BroadcasterId}: {Error} ({Code}) — reporting 0",
                    tenantId,
                    subscriberResult.ErrorMessage,
                    subscriberResult.ErrorCode
                );

            // Channel info uses app token (no scope required) — always shows the real Twitch title/game.
            Result<TwitchChannelInformation> channelInfoResult =
                await _channels.GetChannelInformationAsync(tenantId, ct);
            twitchTitle = channelInfoResult.IsSuccess ? channelInfoResult.Value.Title : null;
            twitchGame = channelInfoResult.IsSuccess ? channelInfoResult.Value.GameName : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Dashboard stats: Helix calls failed for {BroadcasterId} — returning degraded stats",
                tenantId
            );
        }

        // Today's local counters (UTC day) — real rows, never Helix-dependent: distinct hashed chatters
        // and the supporter events the new ingest recorded. A mixed-currency day reports the count with a
        // NULL amount rather than a meaningless cross-currency sum.
        DateOnly today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        DateTime todayStart = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        int chattersToday = await _db.ChannelChatterDays.CountAsync(
            c => c.BroadcasterId == tenantId && c.ActivityDate == today,
            ct
        );
        List<(long? Amount, string? Currency)> supporterToday = (
            await _db
                .SupporterEvents.Where(e =>
                    e.BroadcasterId == tenantId && e.ReceivedAt >= todayStart
                )
                .Select(e => new { e.AmountMinor, e.Currency })
                .ToListAsync(ct)
        )
            .Select(e => (e.AmountMinor, e.Currency))
            .ToList();
        int supporterEventsToday = supporterToday.Count;
        (long? supporterAmountToday, string? supporterCurrency) = AggregateSupporterAmounts(
            supporterToday
        );

        List<string> platformsLive = await ResolvePlatformsLiveAsync(_db, tenantId, ct);

        ChannelContext? ctx = _registry.Get(tenantId);

        if (ctx is not null)
        {
            long? uptime =
                ctx.IsLive && ctx.WentLiveAt.HasValue
                    ? (long)(_timeProvider.GetUtcNow() - ctx.WentLiveAt.Value).TotalSeconds
                    : null;

            // Live viewer count is kept fresh in the registry by StreamStatusPollingService — populated at startup
            // and refreshed every couple of minutes — so no per-request Helix call is needed here (0 when offline).
            int viewerCount = ctx.ViewerCount;

            DashboardStatsDto stats = new()
            {
                IsLive = ctx.IsLive,
                StreamTitle = twitchTitle ?? ctx.CurrentTitle,
                GameName = twitchGame ?? ctx.CurrentGame,
                ViewerCount = viewerCount,
                FollowerCount = followerCount,
                SubscriberCount = subscriberCount,
                ChattersToday = chattersToday,
                SupporterEventsToday = supporterEventsToday,
                SupporterAmountMinorToday = supporterAmountToday,
                SupporterCurrency = supporterCurrency,
                PlatformsLive = platformsLive,
                CommandsUsed = ctx.CommandsUsed,
                MessagesCount = ctx.MessageCount,
                Uptime = uptime,
            };

            return Ok(new StatusResponseDto<DashboardStatsDto> { Data = stats });
        }

        // Channel not currently active in registry — fall back to DB for basic info.
        Result<ChannelDto> result = await _channelService.GetAsync(channelId, ct);
        if (result.IsFailure)
            return ResultResponse(result);

        ChannelDto channel = result.Value;
        DashboardStatsDto fallback = new()
        {
            IsLive = channel.IsLive,
            StreamTitle = twitchTitle ?? channel.Title,
            GameName = twitchGame ?? channel.GameName,
            ViewerCount = channel.ViewerCount ?? 0,
            FollowerCount = followerCount,
            SubscriberCount = subscriberCount,
            ChattersToday = chattersToday,
            SupporterEventsToday = supporterEventsToday,
            SupporterAmountMinorToday = supporterAmountToday,
            SupporterCurrency = supporterCurrency,
            PlatformsLive = platformsLive,
            CommandsUsed = 0,
            MessagesCount = 0,
            Uptime = null,
        };

        return Ok(new StatusResponseDto<DashboardStatsDto> { Data = fallback });
    }

    /// <summary>
    /// Returns recent channel activity events.
    /// </summary>
    [HttpGet("{channelId}/activity")]
    [RequireAction("dashboard:read")]
    [ProducesResponseType<StatusResponseDto<List<ActivityEventDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActivity(string channelId, CancellationToken ct)
    {
        if (!Guid.TryParse(channelId, out Guid tenantId))
            return BadRequestResponse("Invalid channel id.");

        // Chat messages (channel.chat.message) are excluded — they live in the Chat page.
        // Activity shows stream milestones only: follows, subs, raids, cheers, redemptions, etc.
        // Pull a few extra rows so that after collapsing duplicate follows below the feed still fills ~20.
        List<ChannelEvent> recent = await _db
            .ChannelEvents.Where(e => e.ChannelId == tenantId && e.Type != "channel.chat.message")
            .OrderByDescending(e => e.CreatedAt)
            .Take(40)
            .ToListAsync(ct);

        // A viewer is either following or not, so a SECOND channel.follow for the same user is a Twitch
        // at-least-once delivery artifact (EventSub redelivers), never a real second follow — collapse those to
        // the most recent (already first by the descending order) so the feed never shows "X followed" twice.
        // Other event types (cheers, subs, raids, redemptions) can legitimately repeat and are kept as-is.
        HashSet<Guid> seenFollowers = [];
        List<ChannelEvent> events = [];
        foreach (ChannelEvent e in recent)
        {
            bool isFollow = e.Type is "channel.follow" or "follow";
            if (isFollow && e.UserId is { } followerId && !seenFollowers.Add(followerId))
                continue;
            events.Add(e);
            if (events.Count >= 20)
                break;
        }

        List<Guid> userIds = events
            .Where(e => e.UserId is not null)
            .Select(e => e.UserId!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, User> users = await _db
            .Users.Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        List<ActivityEventDto> result = events
            .Select(e =>
            {
                string? username = null;
                if (e.UserId is not null && users.TryGetValue(e.UserId.Value, out User? user))
                    username = user.DisplayName;

                // Most events carry no local User row (a follower or raider who never chatted), so the name only
                // ever lived in the event payload — resolving it from there is what turns a wall of "— followed"
                // back into real names.
                username ??= ResolveActorNameFromData(e.Data);

                // Normalize legacy event types imported from the previous bot to their canonical
                // EventSub equivalents so the frontend only needs one switch on the modern names.
                string normalizedType = e.Type switch
                {
                    "raid" => "channel.raid",
                    "follow" => "channel.follow",
                    "subscribe" => "channel.subscribe",
                    "cheer" => "channel.cheer",
                    _ => e.Type,
                };

                return new ActivityEventDto(
                    e.Id,
                    normalizedType,
                    e.UserId?.ToString(),
                    username,
                    e.Data,
                    e.CreatedAt
                );
            })
            .ToList();

        return Ok(new StatusResponseDto<List<ActivityEventDto>> { Data = result });
    }

    // The actor's name is carried in the event payload — modern EventSub events store it under
    // actorDisplay/actorLogin (raids also fromDisplayName/fromLogin), legacy imported events under user/user.name.
    // Display-name fields are tried before login fields so the feed shows "R2_ADHD2", not "r2_adhd2".
    private static readonly string[] ActorNameFields =
    [
        "actorDisplay",
        "userDisplayName",
        "fromDisplayName",
        "user",
        "actorLogin",
        "userLogin",
        "fromLogin",
        "user.name",
    ];

    /// <summary>
    /// A day's supporter money as ONE honest number: the minor-unit total + its currency when every
    /// amount-bearing event shares a single currency; (null, null) for an amount-less or mixed-currency day —
    /// never a meaningless cross-currency sum.
    /// </summary>
    internal static (long? AmountMinor, string? Currency) AggregateSupporterAmounts(
        IReadOnlyList<(long? Amount, string? Currency)> events
    )
    {
        List<string> currencies =
        [
            .. events
                .Where(e => e.Amount is not null && e.Currency is not null)
                .Select(e => e.Currency!)
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
        if (currencies.Count != 1)
            return (null, null); // no amounts at all, or a mixed-currency day.

        long total = events.Where(e => e.Amount is not null).Sum(e => e.Amount!.Value);
        return (total, currencies[0]);
    }

    /// <summary>
    /// The platforms the channel's OWNER is live on right now, aggregated across every platform presence
    /// channel they own (the primary Twitch row plus the provisioned YouTube/Kick tenant rows, whose
    /// <c>IsLive</c> the respective live trackers stamp). Only the owner's own channels count — another
    /// streamer's live state never leaks in. Sorted alphabetically for a stable wire order.
    /// </summary>
    internal static async Task<List<string>> ResolvePlatformsLiveAsync(
        IApplicationDbContext db,
        Guid tenantId,
        CancellationToken ct
    )
    {
        Guid ownerUserId = await db
            .Channels.Where(c => c.Id == tenantId)
            .Select(c => c.OwnerUserId)
            .FirstOrDefaultAsync(ct);
        if (ownerUserId == Guid.Empty)
            return [];

        return await db
            .Channels.Where(c => c.OwnerUserId == ownerUserId && c.IsLive)
            .Select(c => c.Provider)
            .Distinct()
            .OrderBy(p => p)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Extracts the actor's display name (falling back to login) from a channel event's JSON payload, so an event
    /// whose actor has no local <see cref="User"/> row still resolves a name. Returns null for a missing/blank
    /// payload, a non-object payload, or one with none of the known name fields.
    /// </summary>
    internal static string? ResolveActorNameFromData(string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
            return null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(data);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            foreach (string field in ActorNameFields)
                if (
                    document.RootElement.TryGetProperty(field, out JsonElement value)
                    && value.ValueKind == JsonValueKind.String
                )
                {
                    string? name = value.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                        return name;
                }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
