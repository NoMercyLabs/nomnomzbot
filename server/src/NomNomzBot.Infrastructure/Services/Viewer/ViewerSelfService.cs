// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Viewer;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Infrastructure.Services.Viewer;

/// <summary>
/// The signed-in viewer's own reads over the analytics profile, the wallet, the music config and the follow
/// lookup. Every query is keyed on the caller's user id; no other viewer's row is ever selected.
/// </summary>
public sealed class ViewerSelfService(
    IApplicationDbContext db,
    IMusicConfigService music,
    IFollowStateService follows
) : IViewerSelfService
{
    public async Task<Result<IReadOnlyList<WatchedChannelDto>>> ListWatchedChannelsAsync(
        Guid callerUserId,
        CancellationToken ct = default
    )
    {
        List<WatchedChannelDto> rows = await db
            .ViewerProfiles.Where(p => p.ViewerUserId == callerUserId && p.DeletedAt == null)
            .Join(
                db.Channels.Where(c => c.OwnerUserId != callerUserId),
                p => p.BroadcasterId,
                c => c.Id,
                (p, c) =>
                    new WatchedChannelDto(
                        c.Id,
                        c.Name,
                        c.User.DisplayName,
                        c.User.ProfileImageUrl,
                        c.IsLive,
                        p.LastSeenAt
                    )
            )
            .ToListAsync(ct);

        // Sorted in memory: one viewer watches a handful of channels, and a null LastSeenAt sorts last.
        IReadOnlyList<WatchedChannelDto> ordered = rows.OrderByDescending(r => r.LastSeenAt)
            .ToList();
        return Result.Success(ordered);
    }

    public async Task<Result<ViewerSelfSummaryDto>> GetSummaryAsync(
        Guid channelId,
        Guid callerUserId,
        CancellationToken ct = default
    )
    {
        Channel? channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == channelId, ct);
        if (channel is null)
            return Errors.ChannelNotFound<ViewerSelfSummaryDto>(channelId.ToString());

        ViewerProfile? profile = await db.ViewerProfiles.FirstOrDefaultAsync(
            p =>
                p.BroadcasterId == channelId
                && p.ViewerUserId == callerUserId
                && p.DeletedAt == null,
            ct
        );
        bool pointsEnabled = await db.CurrencyConfigs.AnyAsync(
            c => c.BroadcasterId == channelId && c.IsEnabled && c.DeletedAt == null,
            ct
        );
        long? balance = pointsEnabled ? await ReadBalanceAsync(channelId, callerUserId, ct) : null;
        bool songRequestsEnabled = await ReadSongRequestsEnabledAsync(channelId, ct);
        DateTime? followedAt = await ResolveFollowedAtAsync(channelId, callerUserId, ct);

        return Result.Success(
            new ViewerSelfSummaryDto(
                profile?.FirstSeenAt,
                profile?.LastSeenAt,
                profile?.TotalMessages ?? 0,
                profile?.TotalCommandsUsed ?? 0,
                profile?.TotalSongRequests ?? 0,
                pointsEnabled,
                balance,
                songRequestsEnabled,
                followedAt,
                profile?.IsSubscriber == true ? profile.SubTier : null,
                channel.IsLive
            )
        );
    }

    // Reads an existing wallet only; the self-summary never creates an account as a side effect.
    private async Task<long?> ReadBalanceAsync(
        Guid channelId,
        Guid callerUserId,
        CancellationToken ct
    ) =>
        await db
            .CurrencyAccounts.Where(a =>
                a.BroadcasterId == channelId
                && a.ViewerUserId == callerUserId
                && a.DeletedAt == null
            )
            .Select(a => (long?)a.Balance)
            .FirstOrDefaultAsync(ct);

    private async Task<bool> ReadSongRequestsEnabledAsync(Guid channelId, CancellationToken ct)
    {
        Result<MusicConfigDto> config = await music.GetConfigAsync(channelId.ToString(), ct);
        return config is { IsSuccess: true, Value.IsEnabled: true };
    }

    // Cached Helix lookup; any failure means "could not find out", never a failed read.
    private async Task<DateTime?> ResolveFollowedAtAsync(
        Guid channelId,
        Guid callerUserId,
        CancellationToken ct
    )
    {
        string? twitchUserId = await db
            .Users.Where(u => u.Id == callerUserId)
            .Select(u => u.TwitchUserId)
            .FirstOrDefaultAsync(ct);
        if (twitchUserId is null)
            return null;

        try
        {
            FollowLookup lookup = await follows.ResolveAsync(
                channelId,
                AuthEnums.Platform.Twitch,
                twitchUserId,
                ct
            );
            return lookup.State == FollowState.Following ? lookup.FollowedAt?.UtcDateTime : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}
