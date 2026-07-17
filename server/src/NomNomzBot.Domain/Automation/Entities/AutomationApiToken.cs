// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Automation.Entities;

/// <summary>
/// A channel-scoped API token for the external automation surface (automation-api.md §1, schema P.17).
/// The secret itself is never stored — only its SHA-256 <see cref="TokenHash"/>; the plaintext is shown
/// exactly once at issue/rotate time. <see cref="TokenPrefix"/> is the non-secret display handle
/// (<c>nnzb_ak_AB12</c>) the dashboard lists. Scopes bound what the data plane may do
/// (<c>invoke | read | events | chat</c>); <see cref="AllowedPipelineIdsJson"/> optionally pins
/// <c>invoke</c> to specific pipelines. Revocation is a tombstone (<see cref="RevokedAt"/>), distinct
/// from soft-delete, so the audit trail keeps the credential's lifecycle.
/// </summary>
public class AutomationApiToken : SoftDeletableEntity, ITenantScoped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BroadcasterId { get; set; }

    /// <summary>Operator-chosen label; unique per channel.</summary>
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    /// <summary>SHA-256 (lowercase hex) of the full presented secret; unique across the platform.</summary>
    [MaxLength(64)]
    public string TokenHash { get; set; } = null!;

    /// <summary>Non-secret display id — the first characters of the secret (e.g. <c>nnzb_ak_AB12</c>).</summary>
    [MaxLength(16)]
    public string TokenPrefix { get; set; } = null!;

    /// <summary>JSON <c>string[]</c> ⊆ <c>invoke|read|events|chat</c>.</summary>
    public string ScopesJson { get; set; } = "[]";

    /// <summary>JSON <c>Guid[]</c>; null/empty = any pipeline may be invoked.</summary>
    public string? AllowedPipelineIdsJson { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public Guid CreatedByUserId { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;

    [ForeignKey(nameof(CreatedByUserId))]
    public virtual User CreatedByUser { get; set; } = null!;
}
