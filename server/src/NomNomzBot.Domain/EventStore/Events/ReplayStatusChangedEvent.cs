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

namespace NomNomzBot.Domain.EventStore.Events;

/// <summary>
/// Emitted when a replay or backfill run changes state (started, progressed, completed, faulted) so the
/// dashboard activity feed and other handlers can react. Inherits the canonical <see cref="DomainEventBase"/>
/// (the store references that base; it never redefines its members).
/// </summary>
public sealed class ReplayStatusChangedEvent : DomainEventBase
{
    public required Guid ReplayId { get; init; }
    public required string ProjectionName { get; init; }

    /// <summary><c>queued</c>|<c>running</c>|<c>completed</c>|<c>faulted</c>|<c>cancelled</c>.</summary>
    public required string Status { get; init; }
    public required long FromPosition { get; init; }
    public required long ToPosition { get; init; }
    public required long ProcessedCount { get; init; }
    public string? Error { get; init; }
}
