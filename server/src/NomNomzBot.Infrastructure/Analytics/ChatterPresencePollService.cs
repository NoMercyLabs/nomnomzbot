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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Analytics;

/// <summary>
/// Records who is PRESENT in each live channel's chat, not only who spoke. Every few minutes it reads Helix Get
/// Chatters (scope <c>moderator:read:chatters</c>) for each enabled, onboarded channel that is live and stamps
/// <see cref="ViewerProfile.LastSeenAt"/> for every chatter, so a viewer who sits in chat without a message still
/// has a truthful "last seen" on the People page. The stamp only moves forward (a replayed journal or a late
/// activity event never rewinds it) and a viewer without a profile row gets one through the shared
/// <see cref="ViewerResolver"/>. A channel whose token lacks the scope is skipped quietly and logged once.
/// Auto-discovered by <c>AddHostedWorkers</c>.
/// </summary>
public sealed class ChatterPresencePollService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    // Helix returns at most 1000 chatters per page.
    private const int PageSize = 1000;

    // Keeps the IN (...) lookup under the smallest SQL parameter limit of the supported providers.
    private const int LookupChunkSize = 400;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ChatterPresencePollService> _logger;
    private readonly HashSet<Guid> _missingScopeLogged = [];

    public ChatterPresencePollService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<ChatterPresencePollService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                await PollLiveChannelsAsync(
                    scope.ServiceProvider.GetRequiredService<IApplicationDbContext>(),
                    scope.ServiceProvider.GetRequiredService<ITwitchChatAssetsApi>(),
                    scope.ServiceProvider.GetRequiredService<ViewerResolver>(),
                    stoppingToken
                );
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Chatter presence: tick failed; retrying next interval.");
            }
        }
    }

    /// <summary>Stamps the chatters of every enabled, onboarded, live channel. Returns how many channels were read.</summary>
    internal async Task<int> PollLiveChannelsAsync(
        IApplicationDbContext db,
        ITwitchChatAssetsApi chatAssets,
        ViewerResolver resolver,
        CancellationToken ct
    )
    {
        List<Guid> channels = await db
            .Channels.Where(c => c.Enabled && c.IsOnboarded && c.IsLive)
            .Select(c => c.Id)
            .ToListAsync(ct);

        int read = 0;
        foreach (Guid broadcasterId in channels)
        {
            try
            {
                if (await StampChannelAsync(db, chatAssets, resolver, broadcasterId, ct))
                    read++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex,
                    "Chatter presence: failed for {BroadcasterId}; retrying next interval.",
                    broadcasterId
                );
            }
        }

        return read;
    }

    private async Task<bool> StampChannelAsync(
        IApplicationDbContext db,
        ITwitchChatAssetsApi chatAssets,
        ViewerResolver resolver,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        List<TwitchChatter> chatters = [];
        string? cursor = null;
        do
        {
            Result<TwitchPage<TwitchChatter>> page = await chatAssets.GetChattersAsync(
                broadcasterId,
                new(After: cursor, PageSize: PageSize),
                ct
            );
            if (page.IsFailure)
            {
                LogReadFailure(broadcasterId, page);
                if (chatters.Count == 0)
                    return false;
                break;
            }

            chatters.AddRange(page.Value.Items);
            cursor = page.Value.NextCursor;
        } while (cursor is not null);

        DateTime now = _timeProvider.GetUtcNow().UtcDateTime;
        await StampAsync(db, resolver, broadcasterId, chatters, now, ct);
        return true;
    }

    private void LogReadFailure(Guid broadcasterId, Result<TwitchPage<TwitchChatter>> page)
    {
        if (page.ErrorCode == TwitchErrorCodes.MissingScope)
        {
            if (_missingScopeLogged.Add(broadcasterId))
                _logger.LogInformation(
                    "Chatter presence: {BroadcasterId} has not granted moderator:read:chatters; skipping this channel.",
                    broadcasterId
                );
            return;
        }

        _logger.LogWarning(
            "Chatter presence: Get Chatters failed for {BroadcasterId}: {Error} ({Code})",
            broadcasterId,
            page.ErrorMessage,
            page.ErrorCode
        );
    }

    private static async Task StampAsync(
        IApplicationDbContext db,
        ViewerResolver resolver,
        Guid broadcasterId,
        List<TwitchChatter> chatters,
        DateTime now,
        CancellationToken ct
    )
    {
        Dictionary<string, ViewerProfile> existing = [];
        foreach (string[] chunk in chatters.Select(c => c.UserId).Distinct().Chunk(LookupChunkSize))
        {
            List<ViewerProfile> found = await db
                .ViewerProfiles.Where(p =>
                    p.BroadcasterId == broadcasterId && chunk.Contains(p.ViewerTwitchUserId)
                )
                .ToListAsync(ct);
            foreach (ViewerProfile profile in found)
                existing[profile.ViewerTwitchUserId] = profile;
        }

        foreach (TwitchChatter chatter in chatters.DistinctBy(c => c.UserId))
        {
            if (existing.TryGetValue(chatter.UserId, out ViewerProfile? known))
            {
                if (!known.IsAnalyticsOptedOut)
                    MoveSeenForward(known, now);
                continue;
            }

            ViewerProfile? created = await resolver.ResolveAsync(
                broadcasterId,
                AuthEnums.Platform.Twitch,
                chatter.UserId,
                chatter.UserLogin,
                chatter.UserName,
                ct
            );
            if (created is null)
                continue;

            created.UsernameSnapshot ??= chatter.UserLogin;
            created.DisplayNameSnapshot ??= chatter.UserName;
            MoveSeenForward(created, now);
        }

        await db.SaveChangesAsync(ct);
    }

    private static void MoveSeenForward(ViewerProfile profile, DateTime now)
    {
        profile.FirstSeenAt ??= now;
        if (profile.LastSeenAt is null || profile.LastSeenAt < now)
            profile.LastSeenAt = now;
    }
}
