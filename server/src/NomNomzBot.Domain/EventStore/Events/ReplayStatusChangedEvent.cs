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

namespace NomNomzBot.Domain.EventStore.Events;

/// <summary>
/// Emitted when a replay or backfill run changes state (started, progressed, completed, faulted) so the
/// dashboard activity feed and other handlers can react. Inherits the canonical <see cref="DomainEventBase"/>
/// (the store references that base; it never redefines its members).
/// </summary>
[Event(visibility: EventVisibility.Internal)]
public sealed class ReplayStatusChangedEvent : DomainEventBase
{
    /// <summary>The id of the replay run.</summary>
    public required Guid ReplayId { get; init; }

    /// <summary>The name of the projection that the replay rebuilds.</summary>
    public required string ProjectionName { get; init; }

    /// <summary><c>queued</c>|<c>running</c>|<c>completed</c>|<c>faulted</c>|<c>cancelled</c>.</summary>
    public required string Status { get; init; }

    /// <summary>The journal position where the replay starts.</summary>
    public required long FromPosition { get; init; }

    /// <summary>The journal position where the replay stops.</summary>
    public required long ToPosition { get; init; }

    /// <summary>How many events the replay has processed so far.</summary>
    public required long ProcessedCount { get; init; }

    /// <summary>The error text. Empty when the replay has no error.</summary>
    public string? Error { get; init; }
}
