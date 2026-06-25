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
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Commands.Entities;

/// <summary>
/// A condition guard on a <see cref="PipelineStep"/>. All conditions on a step must pass
/// for the step to execute. Unknown <see cref="ConditionType"/> values hard-fail the run
/// (fail-closed semantics). Schema: H.3 (commands-pipelines.md §1).
/// </summary>
public class PipelineStepCondition : BaseEntity
{
    public Guid Id { get; set; }
    public Guid PipelineStepId { get; set; }
    public Guid BroadcasterId { get; set; }

    /// <summary>Condition kind: user_role | random | var_compare | cooldown.</summary>
    [MaxLength(40)]
    public string ConditionType { get; set; } = null!;

    [MaxLength(20)]
    public string? Operator { get; set; }

    [MaxLength(500)]
    public string? LeftOperand { get; set; }

    [MaxLength(500)]
    public string? RightOperand { get; set; }

    /// <summary>When true the condition result is inverted before evaluation.</summary>
    public bool Negate { get; set; }

    /// <summary>Evaluation order among conditions on the same step.</summary>
    public int Order { get; set; }

    [ForeignKey(nameof(PipelineStepId))]
    public virtual PipelineStep Step { get; set; } = null!;
}
