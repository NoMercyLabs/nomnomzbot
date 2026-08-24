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
public class PipelineStepCondition : BaseEntity, ITenantScoped
{
    public Guid Id { get; set; }
    public Guid PipelineStepId { get; set; }
    public Guid BroadcasterId { get; set; }

    /// <summary>
    /// Self-FK to the parent condition-tree node; null = root of this step's condition tree.
    /// Schema: pipeline-tree-and-editor.md §1.2 (E2).
    /// </summary>
    public Guid? ParentConditionId { get; set; }

    /// <summary>
    /// Combinator for a group node's children: and | or. Set only on a group node (a row with
    /// children and no <see cref="ConditionType"/>); null on a leaf node.
    /// </summary>
    [MaxLength(3)]
    public string? GroupOp { get; set; }

    /// <summary>
    /// Condition kind: user_role | random | var_compare | cooldown. Empty string on a group node
    /// (kept non-nullable — the column stays <c>NOT NULL</c> — so the existing engine/read-path
    /// code that treats <see cref="ConditionType"/> as a required <see cref="string"/> keeps
    /// compiling unmodified; a group node is identified by <see cref="GroupOp"/> being non-null,
    /// not by a null <see cref="ConditionType"/>).
    /// </summary>
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
