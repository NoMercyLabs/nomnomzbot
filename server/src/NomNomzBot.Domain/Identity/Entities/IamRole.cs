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
