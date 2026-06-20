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

namespace NomNomzBot.Domain.Identity.Entities;

/// <summary>
/// An append-only record of every platform-IAM access evaluation (roles-permissions schema O.9, Plane C) —
/// who exercised which permission against which tenant, with justification, break-glass flag, and outcome.
/// The source IP is crypto-vaulted (<c>SourceIpCipher</c>, PII-shred). Append-only: a plain entity carrying
/// only <c>OccurredAt</c> (no <c>UpdatedAt</c>/<c>DeletedAt</c>), keyed by a <c>long</c> identity. SaaS-only.
/// </summary>
public class IamAuditLog
{
    public long Id { get; set; }
    public Guid PrincipalId { get; set; }
    public IamPrincipalType PrincipalType { get; set; }
    public string Permission { get; set; } = null!;
    public Guid? TargetBroadcasterId { get; set; }
    public string? TargetResource { get; set; }
    public string? Justification { get; set; }
    public bool BreakGlass { get; set; }
    public IamOutcome Outcome { get; set; }
    public string? SourceIpCipher { get; set; }
    public DateTime OccurredAt { get; set; }
}
