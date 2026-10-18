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

namespace NomNomzBot.Domain.PlatformContent.Events;

/// <summary>
/// An operator changed the platform content catalogue: created a definition, drafted a version,
/// published a version or retired a definition. Platform-scoped, so the inherited <c>BroadcasterId</c>
/// stays <c>Guid.Empty</c>. The operator hub pushes it so every open content page reloads.
/// </summary>
public sealed class PlatformContentChangedEvent : DomainEventBase
{
    /// <summary><c>created</c> | <c>drafted</c> | <c>published</c> | <c>retired</c>.</summary>
    public required string Action { get; init; }

    /// <summary>The definition the change belongs to.</summary>
    public required Guid DefinitionId { get; init; }
}
