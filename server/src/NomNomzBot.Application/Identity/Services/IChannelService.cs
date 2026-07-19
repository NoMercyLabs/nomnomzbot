// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// Application service for channel management: joining, leaving, settings, and onboarding.
/// </summary>
public interface IChannelService
{
    /// <summary>Join a channel so the bot begins listening and responding.</summary>
    Task<Result> JoinAsync(string broadcasterId, CancellationToken cancellationToken = default);

    /// <summary>Leave a channel, stopping all bot activity.</summary>
    Task<Result> LeaveAsync(string broadcasterId, CancellationToken cancellationToken = default);

    /// <summary>Get full channel details by broadcaster ID.</summary>
    Task<Result<ChannelDto>> GetAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get all active (joined) channels.</summary>
    Task<Result<IReadOnlyList<ChannelSummaryDto>>> GetAllActiveAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>Get channels the user has access to, with pagination.
    /// <paramref name="additionalChannelIds"/> merges in extra channel IDs (e.g. from the Twitch API)
    /// so that moderated channels not yet tracked in the DB are still returned.</summary>
    Task<Result<PagedList<ChannelSummaryDto>>> GetChannelsAsync(
        string userId,
        PaginationParams pagination,
        IReadOnlyList<string>? additionalChannelIds = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Update channel settings (prefix, locale, auto-join, etc.).</summary>
    Task<Result<ChannelDto>> UpdateSettingsAsync(
        string broadcasterId,
        UpdateChannelSettingsDto request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get the channel's onboarding "basics" (command prefix, locale, auto-join, timezone).</summary>
    Task<Result<ChannelBasicsDto>> GetBasicsAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Update the channel's "basics" (command prefix, locale, auto-join, timezone) and echo the saved values.
    /// A changed prefix applies to the live chat hot path without a restart.
    /// </summary>
    Task<Result<ChannelBasicsDto>> UpdateBasicsAsync(
        string broadcasterId,
        UpdateChannelSettingsDto request,
        CancellationToken cancellationToken = default
    );

    /// <summary>Get the channel's built-in-command personality tone.</summary>
    Task<Result<ChannelPersonalityDto>> GetPersonalityAsync(
        string broadcasterId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Set the channel's built-in-command personality tone. Validates the tone against
    /// <c>PersonalityTone.All</c>, persists it, and refreshes the in-memory registry so live chat picks it up.
    /// </summary>
    Task<Result<ChannelPersonalityDto>> SetPersonalityAsync(
        string broadcasterId,
        string personality,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Onboards a channel — creates the record on first onboard, or idempotently repairs an existing one — and
    /// publishes <c>ChannelOnboardedEvent</c> either way. That single event is the entire onboarding fan-out:
    /// every default (rewards, mods/VIPs/subs, channel info, owner profile, event responses, banned-user
    /// import, bot mod-join, default builtin commands, EventSub subscribe) is seeded by an auto-discovered
    /// <c>IEventHandler&lt;ChannelOnboardedEvent&gt;</c>, not by this method — the same single path the
    /// Twitch-OAuth login flow (<c>AuthService</c>) uses.
    /// </summary>
    Task<Result<ChannelDto>> OnboardAsync(
        string broadcasterId,
        CreateChannelRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Get-or-create a lightweight tenant for a Twitch channel the caller only MODERATES ("Moderator mode").
    /// Unlike <see cref="OnboardAsync"/> this provisions the <c>Channels</c> row WITHOUT marking it onboarded
    /// or publishing <c>ChannelOnboardedEvent</c> — there is no bot presence to seed. It exists purely so
    /// tenant resolution (Gate 1) can admit a moderator to a channel the bot is not installed on. Returns the
    /// internal tenant <see cref="Guid"/>. Idempotent: an already-existing channel (onboarded or not) is
    /// returned unchanged.
    /// </summary>
    Task<Result<Guid>> EnsureModeratedTenantAsync(
        string twitchBroadcasterId,
        string login,
        string displayName,
        Guid ownerUserId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Delete a channel and clean up all associated data.</summary>
    Task<Result> DeleteAsync(string broadcasterId, CancellationToken cancellationToken = default);

    /// <summary>Resolve a channel by its overlay token (for widget auth).</summary>
    Task<ChannelOverlayInfo?> GetByOverlayTokenAsync(
        string token,
        CancellationToken cancellationToken = default
    );
}
