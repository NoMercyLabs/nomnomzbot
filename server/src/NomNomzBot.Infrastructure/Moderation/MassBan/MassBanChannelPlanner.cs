// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Moderation.MassBan;

/// <summary>
/// Decides what a mass ban does in each channel a moderator moderates (chat-client.md §3.5). The preview and the
/// request both read this one plan, so the list a moderator reviews is the list that runs.
/// </summary>
public sealed class MassBanChannelPlanner
{
    private readonly IOperatorModeratedChannelResolver _channels;
    private readonly IChannelAccessService _channelAccess;
    private readonly MassBanLiveChannels _liveChannels;
    private readonly IApplicationDbContext _db;

    public MassBanChannelPlanner(
        IOperatorModeratedChannelResolver channels,
        IChannelAccessService channelAccess,
        MassBanLiveChannels liveChannels,
        IApplicationDbContext db
    )
    {
        _channels = channels;
        _channelAccess = channelAccess;
        _liveChannels = liveChannels;
        _db = db;
    }

    /// <summary>One plan row per moderated channel, own channel first. Fails when Twitch cannot list them or their live state.</summary>
    public async Task<Result<IReadOnlyList<MassBanChannelPlan>>> PlanAsync(
        Guid operatorUserId,
        MassBanScope scope,
        CancellationToken ct = default
    )
    {
        Result<IReadOnlyList<TwitchModeratedChannel>> moderated = await _channels.ResolveAsync(
            operatorUserId,
            ct
        );
        if (moderated.IsFailure)
            return moderated.WithValue<IReadOnlyList<MassBanChannelPlan>>(default!);

        Result<HashSet<string>> live = await _liveChannels.FindLiveAsync(
            [.. moderated.Value.Select(c => c.BroadcasterId)],
            ct
        );
        if (live.IsFailure)
            return live.WithValue<IReadOnlyList<MassBanChannelPlan>>(default!);

        Guid ownChannelId = await _channelAccess.ResolveOwnChannelAsync(
            operatorUserId.ToString(),
            ct
        );
        Dictionary<string, Channel> rows = await LoadChannelRowsAsync(moderated.Value, ct);
        HashSet<string> attacked = new(
            scope.AttackedChannelLogins,
            StringComparer.OrdinalIgnoreCase
        );
        HashSet<string> excluded = new(
            scope.ExcludedChannelLogins,
            StringComparer.OrdinalIgnoreCase
        );

        List<MassBanChannelPlan> plans = new(moderated.Value.Count);
        foreach (TwitchModeratedChannel channel in moderated.Value)
        {
            Channel? row = rows.GetValueOrDefault(channel.BroadcasterId);
            Channel? served = row is not null && IsServed(row) ? row : null;
            bool isOwn = row is not null && row.Id == ownChannelId;
            bool isAttacked = attacked.Contains(channel.BroadcasterLogin);
            bool isLive = live.Value.Contains(channel.BroadcasterId);
            string status = StatusFor(
                excluded.Contains(channel.BroadcasterLogin),
                optedOut: row is { AcceptsModeratorMassBans: false },
                holdsWhileLive: !isOwn && !isAttacked,
                isLive,
                usesBot: served is not null
            );
            plans.Add(new(channel, served, isOwn, isAttacked, isLive, status));
        }
        return Result.Success<IReadOnlyList<MassBanChannelPlan>>(plans);
    }

    // The owner's opt-out and the moderator's exclusion win over everything; after that only a live channel waits.
    private static string StatusFor(
        bool excluded,
        bool optedOut,
        bool holdsWhileLive,
        bool isLive,
        bool usesBot
    )
    {
        if (excluded)
            return MassBanChannelStatus.Excluded;
        if (optedOut)
            return MassBanChannelStatus.OptedOut;
        if (!holdsWhileLive || !isLive)
            return MassBanChannelStatus.Banning;
        return usesBot
            ? MassBanChannelStatus.AwaitingApproval
            : MassBanChannelStatus.HeldUntilOffline;
    }

    // A channel row per Twitch id; when one Twitch channel has several rows, the one the bot serves speaks for it.
    private async Task<Dictionary<string, Channel>> LoadChannelRowsAsync(
        IReadOnlyList<TwitchModeratedChannel> moderated,
        CancellationToken ct
    )
    {
        List<string> twitchIds = [.. moderated.Select(c => c.BroadcasterId)];
        List<Channel> rows = await _db
            .Channels.AsNoTracking()
            .Where(c => c.TwitchChannelId != null && twitchIds.Contains(c.TwitchChannelId))
            .ToListAsync(ct);
        return rows.GroupBy(c => c.TwitchChannelId!)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(IsServed).First());
    }

    // Only a channel the bot serves can hear a notice or answer with !allow massban.
    private static bool IsServed(Channel channel) =>
        channel.Enabled && channel.IsOnboarded && channel.Status == AuthEnums.ChannelStatus.Active;
}

/// <summary>
/// One channel of a mass ban: the Twitch channel, its bot channel when the bot serves it, and its
/// <see cref="MassBanChannelStatus"/>.
/// </summary>
public sealed record MassBanChannelPlan(
    TwitchModeratedChannel Channel,
    Channel? ServedChannel,
    bool IsOwnChannel,
    bool IsAttacked,
    bool IsLive,
    string Status
)
{
    /// <summary>The own and attacked channels ban even while live; every other channel waits while it is live.</summary>
    public bool HoldsWhileLive => !IsOwnChannel && !IsAttacked;

    /// <summary>Whether a batch is created for this channel at all.</summary>
    public bool IsIncluded =>
        Status is not (MassBanChannelStatus.Excluded or MassBanChannelStatus.OptedOut);
}
