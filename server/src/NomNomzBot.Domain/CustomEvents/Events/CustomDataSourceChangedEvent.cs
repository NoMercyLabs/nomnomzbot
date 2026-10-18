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
