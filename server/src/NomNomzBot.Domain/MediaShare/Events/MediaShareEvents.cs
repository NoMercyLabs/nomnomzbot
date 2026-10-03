// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.MediaShare.Events;

// DomainEventBase is a class, so these are sealed CLASSES (records may not inherit a non-record class).

/// <summary>A viewer submitted a clip/video to the media-share queue (media-share.md §2).</summary>
public sealed class MediaShareSubmittedEvent : DomainEventBase
{
    /// <summary>The id of the request.</summary>
    public required Guid RequestId { get; init; }

    /// <summary>The id of the viewer who made the request.</summary>
    public required Guid RequesterUserId { get; init; }

    /// <summary>Where the media comes from, as text.</summary>
    public required string SourceType { get; init; }

    /// <summary>True when the request was approved without a manual review.</summary>
    public required bool AutoApproved { get; init; }
}

/// <summary>
/// A media-share item's playback state changed — drives the overlay (media-share.md §2).
/// <see cref="Status"/> ∈ approved | playing | played | skipped.
/// </summary>
public sealed class MediaSharePlaybackChangedEvent : DomainEventBase
{
    /// <summary>The id of the request.</summary>
    public required Guid RequestId { get; init; }

    /// <summary>The new playback status, as text.</summary>
    public required string Status { get; init; }
}
