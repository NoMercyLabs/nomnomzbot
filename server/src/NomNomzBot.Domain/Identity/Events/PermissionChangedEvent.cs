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

/// <summary>When a permission value changes for a user or a role.</summary>
public sealed class PermissionChangedEvent : DomainEventBase
{
    /// <summary>What kind of subject got the change.</summary>
    public required string SubjectType { get; init; }

    /// <summary>The id of the user or role that got the change.</summary>
    public required string SubjectId { get; init; }

    /// <summary>What kind of thing the permission is for.</summary>
    public required string ResourceType { get; init; }

    /// <summary>The id of the thing the permission is for.</summary>
    public required string ResourceId { get; init; }

    /// <summary>The new permission value: 1 for allow, 0 for deny.</summary>
    public required int NewPermissionValue { get; init; }
}
