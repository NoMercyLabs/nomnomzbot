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

    /// <summary>How long a profile stays fresh before it is re-read. Long enough that the whole viewer base
    /// costs a trickle of cheap Helix reads rather than a sweep, short enough that a rename or a new avatar
    /// lands the same day.</summary>
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromHours(12);

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

            // Two populations, one pass: never-hydrated viewers (no avatar yet) AND profiles that have gone
            // stale. Selecting only the former meant a profile was fetched ONCE and never again, so a viewer
            // who changed their display name or avatar kept the old one forever, everywhere it is shown.
            // Recently-seen first, so visible chatters refresh before long-idle ones.
            DateTime staleBefore = DateTime.UtcNow - RefreshAfter;
            List<User> pending = await db
                .Users.Where(u =>
                    u.TwitchUserId != ""
                    && (
                        u.ProfileImageUrl == null
                        || u.ProfileRefreshedAt == null
                        || u.ProfileRefreshedAt < staleBefore
                    )
                )
                .OrderByDescending(u => u.LastSeenAt)
                .Take(MaxUsersPerTick)
                .ToListAsync(ct);

            if (pending.Count == 0)
                return;

            ITwitchUsersApi usersApi = scope.ServiceProvider.GetRequiredService<ITwitchUsersApi>();

            int hydrated = 0;
            // Counted separately from `hydrated`: a profile that came back IDENTICAL still had its
            // refreshed-at stamped, and that stamp has to be saved or the same rows are re-fetched on every
            // tick forever and the backlog never drains.
            int stamped = 0;
            foreach (User[] batch in pending.Chunk(HelixBatchSize))
            {
                List<string> ids = [.. batch.Select(u => u.TwitchUserId!)];
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
                    if (!byId.TryGetValue(user.TwitchUserId!, out TwitchUser? twitchUser))
                        continue;

                    stamped++;
                    if (ApplyProfile(user, twitchUser))
                        hydrated++;
                }
            }

            if (stamped > 0)
            {
                await db.SaveChangesAsync(ct);
                _logger.LogDebug(
                    "User profile hydration: re-read {Stamped} viewer profile(s) from Helix, {Changed} changed.",
                    stamped,
                    hydrated
                );
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
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

        // The rename case. Twitch lets a user change their login AND their display name, and every surface
        // that shows a viewer reads these — leaving them behind meant the bot addressed people by a name
        // they no longer use. UsernameNormalized moves with Username or lookups by the new name miss.
        if (!string.IsNullOrEmpty(twitchUser.Login) && user.Username != twitchUser.Login)
        {
            user.Username = twitchUser.Login;
            user.UsernameNormalized = twitchUser.Login.ToLowerInvariant();
            changed = true;
        }

        if (
            !string.IsNullOrEmpty(twitchUser.DisplayName)
            && user.DisplayName != twitchUser.DisplayName
        )
        {
            user.DisplayName = twitchUser.DisplayName;
            changed = true;
        }

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

        // Stamped even when nothing changed: the profile WAS re-read, and without this an unchanged viewer
        // would be re-fetched on every single tick forever, which is the whole backlog never draining.
        user.ProfileRefreshedAt = DateTime.UtcNow;
        return changed;
    }
}
