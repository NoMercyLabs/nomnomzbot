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

    /// <summary>Onboard a new channel: create record, join chat, set up defaults.</summary>
    Task<Result<ChannelDto>> OnboardAsync(
        string broadcasterId,
        CreateChannelRequest request,
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
