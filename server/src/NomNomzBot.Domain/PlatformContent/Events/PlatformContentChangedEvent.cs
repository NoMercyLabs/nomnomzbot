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
