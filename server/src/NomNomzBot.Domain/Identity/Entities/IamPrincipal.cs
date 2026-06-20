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
/// A platform-IAM principal (roles-permissions schema C.4, Plane C) — a NoMercy Labs employee (linked to a
/// <c>User</c>) or a service account (authenticated by <c>ServiceAccountKeyHash</c>). The contact email is
/// crypto-vaulted (<c>EmailCipher</c> + <c>SubjectKeyId</c>), never plaintext. SaaS-only.
/// </summary>
public class IamPrincipal : SoftDeletableEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public IamPrincipalType PrincipalType { get; set; }
    public Guid? UserId { get; set; }
    public string Name { get; set; } = null!;
    public string? EmailCipher { get; set; }
    public Guid? SubjectKeyId { get; set; }
    public string? ServiceAccountKeyHash { get; set; }
    public bool IsActive { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
