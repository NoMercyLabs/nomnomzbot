// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
