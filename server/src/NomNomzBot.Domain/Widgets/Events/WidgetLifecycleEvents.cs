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

namespace NomNomzBot.Domain.Widgets.Events;

/// <summary>A widget build compiled successfully — a new active version is live; overlays cache-bust and reload.</summary>
public sealed class WidgetBuildSucceededEvent : DomainEventBase
{
    public required Guid WidgetId { get; init; }
    public required Guid VersionId { get; init; }
    public required int VersionNumber { get; init; }
    public required string ContentHash { get; init; } // 64-char sha256, cache-bust key
}

/// <summary>A widget build failed — surfaced to the editor as a compile error, never silent.</summary>
public sealed class WidgetBuildFailedEvent : DomainEventBase
{
    public required Guid WidgetId { get; init; }
    public required Guid VersionId { get; init; }
    public required int VersionNumber { get; init; }
    public required string BuildError { get; init; }
}

/// <summary>A widget's settings changed — live-pushed to connected overlays (no rebuild).</summary>
public sealed class WidgetSettingsChangedEvent : DomainEventBase
{
    public required Guid WidgetId { get; init; }
}

/// <summary>A gallery item's review status changed (platform plane; <c>BroadcasterId</c> is the global sentinel).</summary>
public sealed class WidgetGalleryItemStatusChangedEvent : DomainEventBase
{
    public required Guid GalleryItemId { get; init; }
    public required string FromStatus { get; init; }
    public required string ToStatus { get; init; }
    public string? NewPinnedCommitSha { get; init; }
    public required Guid ChangedByUserId { get; init; }
}
