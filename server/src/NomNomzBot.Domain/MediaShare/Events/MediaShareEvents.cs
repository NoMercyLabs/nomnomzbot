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
    public required Guid RequestId { get; init; }
    public required Guid RequesterUserId { get; init; }
    public required string SourceType { get; init; }
    public required bool AutoApproved { get; init; }
}

/// <summary>
/// A media-share item's playback state changed — drives the overlay (media-share.md §2).
/// <see cref="Status"/> ∈ approved | playing | played | skipped.
/// </summary>
public sealed class MediaSharePlaybackChangedEvent : DomainEventBase
{
    public required Guid RequestId { get; init; }
    public required string Status { get; init; }
}
