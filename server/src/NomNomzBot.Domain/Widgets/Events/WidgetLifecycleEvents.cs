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

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Widgets.Events;

/// <summary>A widget build compiled successfully — a new active version is live; overlays cache-bust and reload.</summary>
public sealed class WidgetBuildSucceededEvent : DomainEventBase
{
    /// <summary>The id of the widget that was built.</summary>
    public required Guid WidgetId { get; init; }

    /// <summary>The id of the new widget version.</summary>
    public required Guid VersionId { get; init; }

    /// <summary>The version number of the widget.</summary>
    public required int VersionNumber { get; init; }

    /// <summary>A 64-character fingerprint (SHA-256) of the built widget. It changes when the widget changes.</summary>
    public required string ContentHash { get; init; } // 64-char sha256, cache-bust key
}

/// <summary>A widget build failed — surfaced to the editor as a compile error, never silent.</summary>
public sealed class WidgetBuildFailedEvent : DomainEventBase
{
    /// <summary>The id of the widget that failed to build.</summary>
    public required Guid WidgetId { get; init; }

    /// <summary>The id of the widget version that failed to build.</summary>
    public required Guid VersionId { get; init; }

    /// <summary>The version number of the widget.</summary>
    public required int VersionNumber { get; init; }

    /// <summary>The error message from the failed build.</summary>
    public required string BuildError { get; init; }
}

/// <summary>A widget's settings changed — live-pushed to connected overlays (no rebuild).</summary>
public sealed class WidgetSettingsChangedEvent : DomainEventBase
{
    /// <summary>The id of the widget.</summary>
    public required Guid WidgetId { get; init; }

    /// <summary>The widget settings now in effect, as names with values.</summary>
    public required IReadOnlyDictionary<string, object> Settings { get; init; }
}

/// <summary>A gallery item's review status changed (platform plane; <c>BroadcasterId</c> is the global sentinel).</summary>
public sealed class WidgetGalleryItemStatusChangedEvent : DomainEventBase
{
    /// <summary>The id of the gallery item.</summary>
    public required Guid GalleryItemId { get; init; }

    /// <summary>The review status before the change.</summary>
    public required string FromStatus { get; init; }

    /// <summary>The review status after the change.</summary>
    public required string ToStatus { get; init; }

    /// <summary>The commit the item is now pinned to. Empty when the pin did not change.</summary>
    public string? NewPinnedCommitSha { get; init; }

    /// <summary>The id of the user who changed the status.</summary>
    public required Guid ChangedByUserId { get; init; }
}
