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

namespace NomNomzBot.Domain.Identity.Events;

/// <summary>
/// An operator changed platform access: provisioned a principal, assigned or revoked a role, or
/// deactivated or reactivated a principal. Platform-scoped, so the inherited <c>BroadcasterId</c>
/// stays <c>Guid.Empty</c>. The operator hub pushes it so every open access page reloads.
/// </summary>
public sealed class PlatformAccessChangedEvent : DomainEventBase
{
    /// <summary><c>created</c> | <c>assigned</c> | <c>revoked</c> | <c>deactivated</c> | <c>reactivated</c>.</summary>
    public required string Action { get; init; }

    /// <summary>The principal the change belongs to. Null when only the assignment is known (a revoke).</summary>
    public Guid? PrincipalId { get; init; }

    /// <summary>The role assignment the change belongs to. Null when the change is not about one assignment.</summary>
    public Guid? AssignmentId { get; init; }
}
