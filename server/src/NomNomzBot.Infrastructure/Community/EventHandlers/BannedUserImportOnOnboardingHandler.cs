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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Community.EventHandlers;

/// <summary>
/// Onboarding seed job (Community / ban-registry domain): when a channel finishes onboarding, pulls every
/// currently-banned user from Helix (all pages) and registers a <c>Configuration</c> row keyed
/// <c>ban:{twitchUserId}</c> — the registry the Community page's ban badge/filter and per-viewer ban-history
/// detail read (<c>CommunityController</c>). Requires the scope <see cref="ITwitchModerationApi.GetBannedUsersAsync"/>
/// itself gates on (moderation:read); gracefully logs a warning and exits when the scope is absent, exactly like
/// the subscriber/VIP standing handlers. Idempotent: an existing <c>ban:{userId}</c> row is left untouched
/// (never overwrites a manually-edited ban record), so a re-onboard or the startup backfill never duplicates or
/// clobbers a row. Independently resilient — caught + logged, never propagated. Uses
/// <see cref="IServiceScopeFactory"/> to create its own <see cref="IApplicationDbContext"/> scope, matching
/// <c>EarningRuleSeedOnOnboardingHandler</c>'s isolation for a multi-row insert.
/// </summary>
public sealed class BannedUserImportOnOnboardingHandler(
    IServiceScopeFactory scopeFactory,
    ITwitchModerationApi moderation,
    TimeProvider timeProvider,
    ILogger<BannedUserImportOnOnboardingHandler> logger
) : IEventHandler<ChannelOnboardedEvent>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task HandleAsync(ChannelOnboardedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        logger.LogInformation(
            "Onboarding seed (banned users): importing banned users from Twitch for {BroadcasterId} ({Name})",
            @event.BroadcasterId,
            @event.Name
        );

        try
        {
            List<TwitchBannedUser> bannedUsers = [];
            string? cursor = null;

            do
            {
                TwitchPageRequest page = new() { After = cursor };
                Result<TwitchPage<TwitchBannedUser>> result = await moderation.GetBannedUsersAsync(
                    @event.BroadcasterId,
                    page,
                    ct
                );

                if (result.IsFailure)
                {
                    logger.LogWarning(
                        "Onboarding seed (banned users): Helix banned-users call failed for {BroadcasterId}: {Error} ({Code}) — the ban registry will be sourced from future ban/unban actions instead",
                        @event.BroadcasterId,
                        result.ErrorMessage,
                        result.ErrorCode
                    );
                    return;
                }

                bannedUsers.AddRange(result.Value.Items);
                cursor = result.Value.NextCursor;
            } while (!string.IsNullOrEmpty(cursor));

            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IApplicationDbContext db =
                scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

            HashSet<string> existingKeys = (
                await db
                    .Configurations.Where(c =>
                        c.BroadcasterId == @event.BroadcasterId && c.Key.StartsWith("ban:")
                    )
                    .Select(c => c.Key)
                    .ToListAsync(ct)
            ).ToHashSet();

            int imported = 0;
            foreach (TwitchBannedUser ban in bannedUsers)
            {
                string key = $"ban:{ban.UserId}";
                if (existingKeys.Contains(key))
                    continue;

                db.Configurations.Add(
                    new Configuration
                    {
                        BroadcasterId = @event.BroadcasterId,
                        Key = key,
                        Value = JsonSerializer.Serialize(
                            new
                            {
                                userId = ban.UserId,
                                username = ban.UserLogin,
                                displayName = ban.UserName,
                                profileImageUrl = (string?)null,
                                reason = ban.Reason,
                                bannedBy = ban.ModeratorName,
                                bannedAt = timeProvider.GetUtcNow().UtcDateTime,
                            },
                            JsonOptions
                        ),
                    }
                );
                imported++;
            }

            if (imported > 0)
                await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Onboarding seed (banned users): completed for {BroadcasterId} — {Count} ban(s) imported",
                @event.BroadcasterId,
                imported
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Onboarding seed (banned users): failed for {BroadcasterId}",
                @event.BroadcasterId
            );
        }
    }
}
