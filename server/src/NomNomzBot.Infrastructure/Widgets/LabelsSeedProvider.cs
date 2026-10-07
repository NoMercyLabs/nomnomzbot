// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Infrastructure.Widgets;

/// <summary>
/// Gives `labels` its real value after a reload, one frame per mode in the shape the widget already reads live.
/// <c>follower_count</c> and <c>sub_count</c> come from Twitch as an absolute <c>count</c> frame. <c>latest_follower</c>
/// and <c>latest_sub</c> come from this channel's newest stored follow, sub or resub row, as a <c>follow</c>,
/// <c>subscription</c> or <c>resub</c> frame. <c>top_cheerer</c> sends the cheer board of the current stream (offline
/// gives no frames). A failed Twitch call or an empty source gives no frame, so the label keeps its idle dash instead
/// of a 0.
/// </summary>
internal sealed class LabelsSeedProvider(
    ITwitchChannelsApi channels,
    ITwitchSubscriptionsApi subscriptions,
    IApplicationDbContext db,
    IChannelRegistry registry
) : IWidgetSeedProvider
{
    private const string FollowType = "channel.follow";
    private const string SubscribeType = "channel.subscribe";
    private const string ResubType = "channel.subscription.message";

    public string NaturalKey => "labels";

    public async Task<IReadOnlyList<WidgetSeedFrame>> SeedAsync(
        Guid broadcasterId,
        Widget widget,
        CancellationToken cancellationToken
    )
    {
        string mode = WidgetSettingText.Read(widget, "label") ?? "latest_follower";
        return mode switch
        {
            "follower_count" => CountFrame(
                "followers",
                await channels.GetChannelFollowerCountAsync(broadcasterId, cancellationToken)
            ),
            "sub_count" => CountFrame(
                "subs",
                await subscriptions.GetSubscriberCountAsync(broadcasterId, cancellationToken)
            ),
            "latest_follower" => await LatestFollowerAsync(broadcasterId, cancellationToken),
            "latest_sub" => await LatestSubAsync(broadcasterId, cancellationToken),
            "top_cheerer" => await TopCheererAsync(broadcasterId, cancellationToken),
            _ => [],
        };
    }

    private static IReadOnlyList<WidgetSeedFrame> CountFrame(string metric, Result<int> total) =>
        total.IsFailure ? [] : [Frame("count", new CountWidgetEventPayload(metric, total.Value))];

    private async Task<IReadOnlyList<WidgetSeedFrame>> LatestFollowerAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        ChannelEvent? row = await NewestAsync(broadcasterId, [FollowType], cancellationToken);
        if (row is null || ReadVariables(row) is not JsonElement vars)
            return [];

        string? name = EventVariables.ReadString(vars, "user");
        if (string.IsNullOrEmpty(name))
            return [];

        DateTimeOffset? followedAt = DateTimeOffset.TryParse(
            EventVariables.ReadString(vars, "followed_at"),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out DateTimeOffset parsed
        )
            ? parsed
            : null;
        return
        [
            Frame(
                "follow",
                new FollowSeedPayload(
                    EventVariables.ReadString(vars, "user.id") ?? string.Empty,
                    name,
                    EventVariables.ReadString(vars, "user.name") ?? string.Empty,
                    followedAt
                )
            ),
        ];
    }

    private async Task<IReadOnlyList<WidgetSeedFrame>> LatestSubAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        ChannelEvent? row = await NewestAsync(
            broadcasterId,
            [SubscribeType, ResubType],
            cancellationToken
        );
        if (row is null || ReadVariables(row) is not JsonElement vars)
            return [];

        string? name = EventVariables.ReadString(vars, "user");
        if (string.IsNullOrEmpty(name))
            return [];

        string userId = EventVariables.ReadString(vars, "user.id") ?? string.Empty;
        string tier = EventVariables.ReadString(vars, "tier") ?? string.Empty;
        if (row.Type == SubscribeType)
            return [Frame("subscription", new SubscriptionSeedPayload(userId, name, tier))];

        string? message = EventVariables.ReadString(vars, "message");
        return
        [
            Frame(
                "resub",
                new ResubSeedPayload(
                    userId,
                    name,
                    tier,
                    ReadInt(vars, "months"),
                    ReadInt(vars, "streak"),
                    string.IsNullOrEmpty(message) ? null : message
                )
            ),
        ];
    }

    private async Task<IReadOnlyList<WidgetSeedFrame>> TopCheererAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        DateTimeOffset? wentLive = registry.Get(broadcasterId)?.WentLiveAt;
        if (wentLive is null)
            return [];

        IReadOnlyList<CheerSeedPayload> board = await CheerBoard.LoadAsync(
            db,
            broadcasterId,
            wentLive,
            cancellationToken
        );
        return [.. board.Select(c => Frame("cheer", c))];
    }

    private Task<ChannelEvent?> NewestAsync(
        Guid broadcasterId,
        string[] types,
        CancellationToken cancellationToken
    ) =>
        db
            .ChannelEvents.AsNoTracking()
            .Where(e => e.ChannelId == broadcasterId && types.Contains(e.Type))
            .OrderByDescending(e => e.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    private static JsonElement? ReadVariables(ChannelEvent row)
    {
        if (string.IsNullOrEmpty(row.Data))
            return null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(row.Data);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int ReadInt(JsonElement vars, string key) =>
        int.TryParse(
            EventVariables.ReadString(vars, key),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int value
        )
            ? value
            : 0;

    private static WidgetSeedFrame Frame(string eventType, object payload) =>
        new(eventType, payload, DateTimeOffset.UtcNow);

    // The wire shapes of the live follow, subscription and resub alerts, as labels.vue reads them.
    private sealed record FollowSeedPayload(
        string UserId,
        string DisplayName,
        string Login,
        DateTimeOffset? FollowedAt
    );

    private sealed record SubscriptionSeedPayload(string UserId, string DisplayName, string Tier);

    private sealed record ResubSeedPayload(
        string UserId,
        string DisplayName,
        string Tier,
        int Months,
        int Streak,
        string? Message
    );
}
