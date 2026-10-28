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

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
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

    /// <summary>
    /// JSON <c>BindingTarget[]</c> (<see cref="BindingTargetJson"/>); null/empty = any target may be invoked.
    /// The migration copied every <see cref="AllowedPipelineIdsJson"/> entry here as a pipeline target; the old
    /// list stays readable. Read the pinned set with <see cref="GetAllowedTargets"/>.
    /// </summary>
    public string? AllowedTargetsJson { get; set; }

    /// <summary>
    /// The targets this token may invoke: <see cref="AllowedTargetsJson"/> when set, else the old
    /// <see cref="AllowedPipelineIdsJson"/> list as pipeline targets (a token issued before the migration
    /// ran). Null = unrestricted.
    /// </summary>
    public IReadOnlyList<BindingTarget>? GetAllowedTargets()
    {
        IReadOnlyList<BindingTarget> targets = BindingTargetJson.DeserializeList(
            AllowedTargetsJson
        );

        if (targets.Count > 0)
            return targets;

        if (string.IsNullOrWhiteSpace(AllowedPipelineIdsJson))
            return null;

        List<Guid> pipelineIds =
            JsonSerializer.Deserialize<List<Guid>>(AllowedPipelineIdsJson) ?? [];

        return pipelineIds.Count == 0
            ? null
            : pipelineIds
                .Select(id => new BindingTarget(BindingTargetKind.Pipeline, id.ToString()))
                .ToList();
    }

    public DateTime? LastUsedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public Guid CreatedByUserId { get; set; }

    [ForeignKey(nameof(BroadcasterId))]
    public virtual Channel Channel { get; set; } = null!;

    [ForeignKey(nameof(CreatedByUserId))]
    public virtual User CreatedByUser { get; set; } = null!;
}
