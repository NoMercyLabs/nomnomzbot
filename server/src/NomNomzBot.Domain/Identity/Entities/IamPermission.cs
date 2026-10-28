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

using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Identity.Entities;

/// <summary>
/// A seeded global platform-IAM permission (roles-permissions schema C.1, Plane C — NoMercy Labs operators).
/// Least-privilege building block bundled into <see cref="IamRole"/>s; <c>IsSensitive</c> flags permissions
/// that demand extra audit/justification. SaaS-only — on self-host Plane C collapses to "owner = full".
/// </summary>
public class IamPermission : BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Key { get; set; } = null!;
    public IamCategory Category { get; set; }
    public bool IsSensitive { get; set; }
    public string? Description { get; set; }
}
