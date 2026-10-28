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

namespace NomNomzBot.Domain.CustomEvents.Events;

/// <summary>
/// A custom data source changed: it was created, edited, deleted or received data. One event per change, so
/// every open custom events page reloads its list (including the last-received time) without polling.
/// </summary>
public sealed class CustomDataSourceChangedEvent : DomainEventBase
{
    /// <summary>The name of the source (its event type is <c>custom.{name}</c>).</summary>
    public required string SourceName { get; init; }

    /// <summary><c>created</c> | <c>updated</c> | <c>deleted</c> | <c>received</c>.</summary>
    public required string Change { get; init; }
}
