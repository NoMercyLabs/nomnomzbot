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

namespace NomNomzBot.Domain.Identity.Entities;

/// <summary>
/// A seeded global platform-IAM role (roles-permissions schema C.2, Plane C) — a named bundle of
/// <see cref="IamPermission"/>s assigned to <see cref="IamPrincipal"/>s. <c>IsSystem</c> marks the canonical
/// seeded roles (not user-deletable). SaaS-only.
/// </summary>
public class IamRole : SoftDeletableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = null!;
    public bool IsSystem { get; set; }
    public string? Description { get; set; }
}
