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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Identity.Jobs;

/// <summary>
/// Backfills the Twitch profile (avatar, account age, broadcaster type, description, staff type) for EVERY chatter,
/// not just the ones who logged in or own a channel. A chatter already gets a bare <see cref="User"/> row on their
/// first message (via <c>PronounHydrationHandler</c> / <c>ChatEarningHandler</c> and, replay-safely, the analytics
/// <c>ViewerResolver</c>), but that row carries only id / login / display name — its
/// <see cref="User.ProfileImageUrl"/> stays null, so chat and the community page show a blank avatar. The only
/// existing hydration paths cover the logging-in user (<c>AuthService</c>) and the channel owner
/// (<c>OwnerProfileSeedOnOnboardingHandler</c>); this worker covers the rest.
/// <para>
/// Each tick it selects un-hydrated users (<see cref="User.ProfileImageUrl"/> is null — Twitch always returns a
/// profile image, even a default, so a filled value is the "done" marker and drops the row out), chunks their Twitch
/// ids ≤100 (the Get Users limit), calls <see cref="ITwitchUsersApi.GetUsersByIdsAsync"/> on the app token, and
/// writes the fields back. Bounded per tick so a large first-run backlog drains gently over several ticks rather than
/// in one burst. Gated on <see cref="IPlatformBotReadinessGate"/>; auto-discovered by <c>AddHostedWorkers</c>.
/// </para>
/// </summary>
public sealed class UserProfileHydrationService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

    // Get Users accepts up to 100 ids per call; cap the per-tick work to a few calls so a large backlog drains over
    // several ticks instead of a burst of Helix traffic (the reads are cheap, but this keeps the worker gentle).
    private const int HelixBatchSize = 100;
    private const int MaxUsersPerTick = 300;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UserProfileHydrationService> _logger;

    // Latches the "waiting for onboarding" log so the dormant path logs once, not on every tick.
    private int _waitingLogged;

    public UserProfileHydrationService(
        IServiceScopeFactory scopeFactory,
        ILogger<UserProfileHydrationService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Self-priming: drain the existing backlog on startup, then top up on every interval tick.
            await HydratePendingAsync(stoppingToken);

            using PeriodicTimer timer = new(Interval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await HydratePendingAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down — end the loop quietly.
        }
    }

    private async Task HydratePendingAsync(CancellationToken ct)
    {
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();

            IPlatformBotReadinessGate gate =
                scope.ServiceProvider.GetRequiredService<IPlatformBotReadinessGate>();
            if (!await gate.IsPlatformBotConfiguredAsync(ct))
            {
                if (Interlocked.Exchange(ref _waitingLogged, 1) == 0)
                    _logger.LogInformation(
                        "User profile hydration: waiting for onboarding before calling Helix."
                    );
                return;
            }
            Interlocked.Exchange(ref _waitingLogged, 0);

            IApplicationDbContext db =
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            // Un-hydrated = no avatar yet. Recently-seen first so visible chatters fill in before long-idle ones.
            List<User> pending = await db
                .Users.Where(u => u.ProfileImageUrl == null && u.TwitchUserId != "")
                .OrderByDescending(u => u.LastSeenAt)
                .Take(MaxUsersPerTick)
                .ToListAsync(ct);

            if (pending.Count == 0)
                return;

            ITwitchUsersApi usersApi = scope.ServiceProvider.GetRequiredService<ITwitchUsersApi>();

            int hydrated = 0;
            foreach (User[] batch in pending.Chunk(HelixBatchSize))
            {
                List<string> ids = batch.Select(u => u.TwitchUserId).ToList();
                Result<IReadOnlyList<TwitchUser>> result = await usersApi.GetUsersByIdsAsync(
                    ids,
                    ct
                );
                if (result.IsFailure)
                {
                    _logger.LogWarning(
                        "User profile hydration: Helix Get Users failed for {Count} id(s): {Error} ({Code}). Retrying next tick.",
                        ids.Count,
                        result.ErrorMessage,
                        result.ErrorCode
                    );
                    continue;
                }

                Dictionary<string, TwitchUser> byId = result.Value.ToDictionary(u => u.Id);
                foreach (User user in batch)
                {
                    if (
                        byId.TryGetValue(user.TwitchUserId, out TwitchUser? twitchUser)
                        && ApplyProfile(user, twitchUser)
                    )
                        hydrated++;
                }
            }

            if (hydrated > 0)
            {
                await db.SaveChangesAsync(ct);
                _logger.LogDebug(
                    "User profile hydration: filled {Count} viewer profile(s) from Helix.",
                    hydrated
                );
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "User profile hydration iteration failed; retrying at the next interval."
            );
        }
    }

    /// <summary>
    /// Writes a Helix <see cref="TwitchUser"/> onto its <see cref="User"/> row, filling only fields Twitch actually
    /// returned (Twitch sends an empty string for an ordinary user's broadcaster/staff type, which must not clobber
    /// the entity default). <see cref="User.AccountCreatedAt"/> is set once (immutable). Returns true when any
    /// field changed, so the caller saves only rows that moved.
    /// </summary>
    internal static bool ApplyProfile(User user, TwitchUser twitchUser)
    {
        bool changed = false;

        if (
            !string.IsNullOrEmpty(twitchUser.ProfileImageUrl)
            && user.ProfileImageUrl != twitchUser.ProfileImageUrl
        )
        {
            user.ProfileImageUrl = twitchUser.ProfileImageUrl;
            changed = true;
        }

        if (
            !string.IsNullOrEmpty(twitchUser.OfflineImageUrl)
            && user.OfflineImageUrl != twitchUser.OfflineImageUrl
        )
        {
            user.OfflineImageUrl = twitchUser.OfflineImageUrl;
            changed = true;
        }

        if (
            !string.IsNullOrEmpty(twitchUser.BroadcasterType)
            && user.BroadcasterType != twitchUser.BroadcasterType
        )
        {
            user.BroadcasterType = twitchUser.BroadcasterType;
            changed = true;
        }

        if (!string.IsNullOrEmpty(twitchUser.Type) && user.Type != twitchUser.Type)
        {
            user.Type = twitchUser.Type;
            changed = true;
        }

        if (
            !string.IsNullOrEmpty(twitchUser.Description)
            && user.Description != twitchUser.Description
        )
        {
            user.Description = twitchUser.Description;
            changed = true;
        }

        if (user.AccountCreatedAt is null && twitchUser.CreatedAt != default)
        {
            user.AccountCreatedAt = twitchUser.CreatedAt.UtcDateTime;
            changed = true;
        }

        return changed;
    }
}
