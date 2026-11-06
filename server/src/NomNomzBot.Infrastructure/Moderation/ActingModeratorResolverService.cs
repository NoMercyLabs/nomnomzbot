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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// The Twitch identity an automated moderation action is signed with, and the raw Twitch broadcaster id
/// every Helix "as operator" call needs alongside it.
/// </summary>
/// <param name="OperatorUserId">
/// The tenant user whose own Twitch token signs the Helix call — the channel owner when the channel is
/// onboarded, otherwise a signed-in moderator who is live on the channel.
/// </param>
/// <param name="BroadcasterTwitchId">The channel's raw Twitch id, never a tenant Guid.</param>
public sealed record ActingModerator(Guid OperatorUserId, string BroadcasterTwitchId);

/// <summary>
/// Picks who an automated moderation action (spam defense, the account-age gate, the heat-threshold
/// auto-timeout, AutoMod, violation escalation) is signed as, so none of them has to resolve a Twitch
/// identity on its own and none of them silently does nothing when the obvious identity is missing.
///
/// <para><b>Standing instruction (Stoney, 2026-11-05):</b> "I want this bot to help me moderate all my
/// channels. If they are not onboarded then everything acts in my name as moderator." A channel that
/// never completed onboarding has no owner token on file, so every automated action that resolved
/// <c>Channel.OwnerUserId</c> and called Helix with it failed every single time — confirmed live against
/// <c>cheesethekenku</c> in the dev DB, where follower-bot spam went un-actioned. The fallback below is
/// the fix: when there is no owner token, act as whichever signed-in moderator is actually live on the
/// channel's chat right now (<see cref="IModeratorChatReaders"/> already tracks exactly that, for the
/// dashboard's own "moderate a channel the bot never joined" feature).</para>
/// </summary>
public interface IActingModeratorResolverService
{
    /// <summary>
    /// Resolves the identity to sign an automated action against <paramref name="broadcasterId"/> as.
    /// Never returns <see cref="Guid.Empty"/> on success, and never claims an identity nothing backs —
    /// a channel with neither an owner token nor a live moderator reader fails clearly instead.
    /// </summary>
    Task<Result<ActingModerator>> ResolveAsync(Guid broadcasterId, CancellationToken ct = default);
}

/// <inheritdoc cref="IActingModeratorResolverService" />
public sealed class ActingModeratorResolverService(
    IApplicationDbContext db,
    IModeratorChatReaders moderatorChatReaders,
    ICurrentUserService? currentUser = null
) : IActingModeratorResolverService
{
    public async Task<Result<ActingModerator>> ResolveAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        ChannelActingIdentity? channel = await db
            .Channels.Where(c => c.Id == broadcasterId)
            .Select(c => new ChannelActingIdentity(c.OwnerUserId, c.IsOnboarded, c.TwitchChannelId))
            .FirstOrDefaultAsync(ct);

        if (channel is null || string.IsNullOrEmpty(channel.TwitchChannelId))
            return Result.Failure<ActingModerator>(
                "This channel has no Twitch broadcaster id on file, so there is no identity to moderate as.",
                "NO_ACTING_IDENTITY"
            );

        // Onboarded: the channel's own owner token signs the action. This is the channel's own automation,
        // not a moderator's personal action, so no dashboard user needs to be in the loop.
        if (channel.IsOnboarded && channel.OwnerUserId != Guid.Empty)
            return Result.Success(
                new ActingModerator(channel.OwnerUserId, channel.TwitchChannelId)
            );

        // Not onboarded: there is no owner token to sign with. The signed-in moderator who is asking acts as
        // themself, on their own Twitch access — the floor for every channel they moderate.
        if (await SignedInModeratorAsync(broadcasterId, ct) is { } asking)
            return Result.Success(new ActingModerator(asking, channel.TwitchChannelId));

        // Otherwise act as whoever is actually live-moderating this channel's chat right now, on their own
        // token — never silently do nothing.
        ModeratorChatReaderStatus? reading = moderatorChatReaders
            .Snapshot()
            .FirstOrDefault(status =>
                status.State == ModeratorChatReaderState.Reading
                && string.Equals(
                    status.TwitchBroadcasterId,
                    channel.TwitchChannelId,
                    StringComparison.OrdinalIgnoreCase
                )
            );

        return reading?.ModeratorUserId is { } moderatorUserId
            ? Result.Success(new ActingModerator(moderatorUserId, channel.TwitchChannelId))
            : Result.Failure<ActingModerator>(
                "This channel is not onboarded and no signed-in moderator is currently reading its "
                    + "chat, so there is no identity to moderate as.",
                "NO_ACTING_IDENTITY"
            );
    }

    // The request's own user, when they hold a management role on the channel (every ManagementRole is at least
    // Moderator, and the column is a string in the database, so there is no rank comparison to make here). An impersonated request never
    // signs as its subject: a support session may not act on Twitch in someone else's name.
    private async Task<Guid?> SignedInModeratorAsync(Guid broadcasterId, CancellationToken ct)
    {
        if (
            currentUser is not { IsAuthenticated: true, Impersonation: null }
            || !Guid.TryParse(currentUser.UserId, out Guid userId)
        )
            return null;

        // IgnoreQueryFilters: the membership belongs to the channel being moderated, not the request's tenant.
        bool holdsRole = await db
            .ChannelMemberships.IgnoreQueryFilters()
            .AnyAsync(
                m => m.BroadcasterId == broadcasterId && m.UserId == userId && m.DeletedAt == null,
                ct
            );
        return holdsRole ? userId : null;
    }

    private sealed record ChannelActingIdentity(
        Guid OwnerUserId,
        bool IsOnboarded,
        string? TwitchChannelId
    );
}
