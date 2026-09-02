// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Trust;

/// <summary>
/// Context data for calculating a user's trust score in a channel.
/// Derived from Records and ChatMessages — no separate storage needed.
/// </summary>
public sealed class TrustContext
{
    /// <summary>Number of successfully queued/approved song requests.</summary>
    public int SuccessfulRequestCount { get; init; }

    /// <summary>Twitch account age in months.</summary>
    public double AccountAgeMonths { get; init; }

    /// <summary>Content item age in months (e.g. track release date).</summary>
    public double ContentAgeMonths { get; init; }

    /// <summary>View count of the requested content item.</summary>
    public long ContentViewCount { get; init; }

    /// <summary>Whether the user is currently following the channel.</summary>
    public bool IsFollowing { get; init; }

    /// <summary>How long the user has been following, in days.</summary>
    public double FollowAgeDays { get; init; }

    /// <summary>True if the user has moderator status in the channel.</summary>
    public bool IsModerator { get; init; }

    /// <summary>True if the user has VIP status in the channel.</summary>
    public bool IsVip { get; init; }

    /// <summary>True if the user is a subscriber.</summary>
    public bool IsSubscriber { get; init; }

    /// <summary>True if the requested content comes from YouTube.</summary>
    public bool IsYouTubeContent { get; init; }

    /// <summary>Total videos on the YouTube channel (for YouTube content only).</summary>
    public int ContentChannelVideoCount { get; init; }

    /// <summary>Total views on the YouTube channel (for YouTube content only).</summary>
    public long ContentChannelTotalViews { get; init; }

    /// <summary>Subscriber count on the YouTube channel (for YouTube content only).</summary>
    public long ContentChannelSubscribers { get; init; }

    /// <summary>Age of the YouTube channel in months (for YouTube content only).</summary>
    public double ContentChannelAgeMonths { get; init; }

    /// <summary>Times this user's songs were skipped by a moderator.</summary>
    public int SkippedByModCount { get; init; }

    /// <summary>Number of timeouts received in this channel.</summary>
    public int TimeoutCount { get; init; }

    /// <summary>Number of bans received in this channel.</summary>
    public int BanCount { get; init; }
}
