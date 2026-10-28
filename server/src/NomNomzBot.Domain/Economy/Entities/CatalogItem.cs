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
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Economy.Entities;

/// <summary>
/// A purchasable store item (economy.md K.10) — costs currency to redeem and fires a <c>SinkType</c> effect
/// (often a pipeline). Optional per-item stock, cooldowns, per-viewer-per-stream limits, and a purchase
/// permission gate (a community-standing token). One per <c>(BroadcasterId, NameNormalized)</c>.
/// </summary>
public class CatalogItem : SoftDeletableEntity, ITenantScoped, IBindingTargetHolder
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid BroadcasterId { get; set; }
    public string Name { get; set; } = null!;
    public string NameNormalized { get; set; } = null!;
    public string? Description { get; set; }
    public string SinkType { get; set; } = null!;
    public long Cost { get; set; }
    public string? IconUrl { get; set; }
    public bool IsEnabled { get; set; }
    public string Permission { get; set; } = "Everyone";
    public Guid? PipelineId { get; set; }

    /// <inheritdoc cref="IBindingTargetHolder.TargetKind"/>
    public BindingTargetKind TargetKind { get; set; } = BindingTargetKind.Pipeline;

    /// <inheritdoc cref="IBindingTargetHolder.TargetCommandId"/>
    public Guid? TargetCommandId { get; set; }

    /// <inheritdoc cref="IBindingTargetHolder.TargetActionType"/>
    [MaxLength(100)]
    public string? TargetActionType { get; set; }

    /// <inheritdoc cref="IBindingTargetHolder.TargetActionParamsJson"/>
    public string? TargetActionParamsJson { get; set; }

    Guid? IBindingTargetHolder.LegacyPipelineId
    {
        get => PipelineId;
        set => PipelineId = value;
    }
    public int CooldownSeconds { get; set; }
    public bool CooldownPerUser { get; set; }
    public int? StockLimit { get; set; }
    public int? StockRemaining { get; set; }
    public int? MaxPerViewerPerStream { get; set; }
    public int SortOrder { get; set; }
}
