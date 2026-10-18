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

namespace NomNomzBot.Domain.Music.Events;

/// <summary>
/// The playback director changed state, or handled an input with a noteworthy reason. One event per
/// transition (music-playback-determinism.md section 2). Journal diagnostics, not a script trigger, so it
/// is Internal.
/// </summary>
[Event(visibility: EventVisibility.Internal)]
public sealed class PlaybackTransitionedEvent : DomainEventBase
{
    /// <summary>The PlaybackStateKind name before the input.</summary>
    public required string From { get; init; }

    /// <summary>The PlaybackStateKind name after the input.</summary>
    public required string To { get; init; }

    /// <summary>The input type name, for example OwnerSkip.</summary>
    public required string Input { get; init; }

    /// <summary>The engine involved, as EngineKind text; null when none.</summary>
    public string? Engine { get; init; }

    /// <summary>The queue row id involved; null when none.</summary>
    public string? QueueItemId { get; init; }

    /// <summary>Why, for example foreign_engine_silenced or stop_unconfirmed.</summary>
    public required string Reason { get; init; }
}
