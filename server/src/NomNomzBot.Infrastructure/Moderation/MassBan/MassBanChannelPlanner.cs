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
        HashSet<string> moderatorOptIns = await LoadModeratorOptInsAsync(operatorUserId, ct);
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
            string? optIn = OptInFor(
                isOwn,
                isAttacked,
                ownerOptedIn: row is { AcceptsModeratorMassBans: true },
                moderatorOptedIn: moderatorOptIns.Contains(channel.BroadcasterId)
            );
            string status = StatusFor(
                excluded.Contains(channel.BroadcasterLogin),
                optedIn: optIn is not null,
                holdsWhileLive: !isOwn && !isAttacked,
                isLive,
                usesBot: served is not null
            );
            plans.Add(new(channel, served, isOwn, isAttacked, isLive, status, optIn));
        }
        plans.AddRange(
            await PlanOwnerTokenChannelsAsync(
                operatorUserId,
                moderated.Value,
                attacked,
                excluded,
                ct
            )
        );
        return Result.Success<IReadOnlyList<MassBanChannelPlan>>(plans);
    }

    // Owner 2026-10-06 (anda_six): a served channel whose owner opted in is reached even when Twitch does not list
    // the operator as its moderator — the bans ride the channel's own token. Only an operator the owner or the
    // platform already trusts gets there: a roster moderator of that channel, or a platform principal.
    private async Task<List<MassBanChannelPlan>> PlanOwnerTokenChannelsAsync(
        Guid operatorUserId,
        IReadOnlyList<TwitchModeratedChannel> moderated,
        HashSet<string> attacked,
        HashSet<string> excluded,
        CancellationToken ct
    )
    {
        HashSet<string> listedByTwitch = [.. moderated.Select(c => c.BroadcasterId)];
        bool platformPrincipal = await _db
            .Users.AsNoTracking()
            .Where(u => u.Id == operatorUserId)
            .Select(u => u.IsPlatformPrincipal)
            .FirstOrDefaultAsync(ct);
        List<Channel> candidates = await _db
            .Channels.AsNoTracking()
            .Where(c =>
                c.AcceptsModeratorMassBans
                && c.TwitchChannelId != null
                && c.OwnerUserId != operatorUserId
                && (
                    platformPrincipal
                    || _db.ChannelModerators.Any(m =>
                        m.ChannelId == c.Id && m.UserId == operatorUserId
                    )
                )
            )
            .ToListAsync(ct);
        List<Channel> reachable =
        [
            .. candidates.Where(c => IsServed(c) && !listedByTwitch.Contains(c.TwitchChannelId!)),
        ];
        if (reachable.Count == 0)
            return [];

        Result<HashSet<string>> live = await _liveChannels.FindLiveAsync(
            [.. reachable.Select(c => c.TwitchChannelId!)],
            ct
        );
        List<MassBanChannelPlan> plans = new(reachable.Count);
        foreach (Channel row in reachable)
        {
            TwitchModeratedChannel channel = new(row.TwitchChannelId!, row.Name, row.Name);
            bool isAttacked = attacked.Contains(row.Name);
            // Unknown live state holds the batch: the executor re-checks before it runs.
            bool isLive = live.IsFailure || live.Value.Contains(row.TwitchChannelId!);
            string status = StatusFor(
                excluded.Contains(row.Name),
                optedIn: true,
                holdsWhileLive: !isAttacked,
                isLive,
                usesBot: true
            );
            plans.Add(
                new(
                    channel,
                    row,
                    IsOwnChannel: false,
                    isAttacked,
                    isLive,
                    status,
                    MassBanOptInSource.Owner,
                    RunsAsBroadcaster: true
                )
            );
        }
        return plans;
    }

    // The strongest reason a channel is in, or null when nobody opted it in (owner 2026-10-06: opt-in only).
    private static string? OptInFor(
        bool isOwn,
        bool isAttacked,
        bool ownerOptedIn,
        bool moderatorOptedIn
    )
    {
        if (isOwn)
            return MassBanOptInSource.Own;
        if (isAttacked)
            return MassBanOptInSource.Attacked;
        if (ownerOptedIn)
            return MassBanOptInSource.Owner;
        return moderatorOptedIn ? MassBanOptInSource.Moderator : null;
    }

    // The moderator's exclusion and a missing opt-in win over everything; after that only a live channel waits.
    private static string StatusFor(
        bool excluded,
        bool optedIn,
        bool holdsWhileLive,
        bool isLive,
        bool usesBot
    )
    {
        if (excluded)
            return MassBanChannelStatus.Excluded;
        if (!optedIn)
            return MassBanChannelStatus.NotOptedIn;
        if (!holdsWhileLive || !isLive)
            return MassBanChannelStatus.Banning;
        return usesBot
            ? MassBanChannelStatus.AwaitingApproval
            : MassBanChannelStatus.HeldUntilOffline;
    }

    private async Task<HashSet<string>> LoadModeratorOptInsAsync(
        Guid operatorUserId,
        CancellationToken ct
    )
    {
        List<string> ids = await _db
            .ModeratorMassBanOptIns.AsNoTracking()
            .Where(o => o.OperatorUserId == operatorUserId)
            .Select(o => o.BroadcasterTwitchId)
            .ToListAsync(ct);
        return [.. ids];
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
    string Status,
    string? OptIn,
    bool RunsAsBroadcaster = false
)
{
    /// <summary>The own and attacked channels ban even while live; every other channel waits while it is live.</summary>
    public bool HoldsWhileLive => !IsOwnChannel && !IsAttacked;

    /// <summary>Whether a batch is created for this channel at all.</summary>
    public bool IsIncluded =>
        Status is not (MassBanChannelStatus.Excluded or MassBanChannelStatus.NotOptedIn);
}
