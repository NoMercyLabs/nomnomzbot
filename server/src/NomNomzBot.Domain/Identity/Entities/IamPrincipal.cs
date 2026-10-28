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
