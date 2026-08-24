// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Commands.Entities;

namespace NomNomzBot.Application.Commands.Services;

/// <summary>
/// Read-time, lossless upcast of a legacy flat-shaped pipeline onto the tree model
/// (pipeline-tree-and-editor.md §6.1, E9). Pure and side-effect free — it never writes to the
/// database; a pipeline never opened in the new tree editor keeps executing from its original flat
/// rows forever (no forced backfill, no migration pass, no risk window, per the spec's binding
/// decision). Calling either method twice on the same source rows yields a structurally identical
/// tree each time (idempotent by construction: no state is mutated).
/// </summary>
public interface IPipelineTreeCompatibilityService
{
    /// <summary>
    /// Upcasts a step's flat, top-level (<c>ParentConditionId == null</c>) condition rows into a
    /// condition tree. If the rows already form a tree (any row already carries a non-null
    /// <see cref="PipelineStepCondition.ParentConditionId"/> or <see cref="PipelineStepCondition.GroupOp"/>),
    /// they are returned unchanged — never re-wrapped. Otherwise a synthetic root node with
    /// <c>GroupOp = "and"</c> is synthesized in-memory (never persisted, <see cref="PipelineStepCondition.Id"/>
    /// left <see cref="Guid.Empty"/>) wrapping every existing leaf as its child, <c>Order</c>-preserved —
    /// semantically identical to today's "every condition must pass" flat-AND evaluation. Zero
    /// input conditions return an empty tree (unchanged "always true" meaning).
    /// </summary>
    IReadOnlyList<PipelineStepCondition> UpcastConditionTree(
        IReadOnlyList<PipelineStepCondition> flatConditions
    );

    /// <summary>
    /// Upcasts a pipeline's legacy single <see cref="Pipeline.TriggerKind"/> column (plus, for
    /// <c>TriggerKind == "command"</c>, the wrapping <see cref="Command"/>'s own trigger phrase) into
    /// the <see cref="PipelineTrigger"/> shape. If the pipeline already carries real
    /// <see cref="Pipeline.Triggers"/> rows, they are returned unchanged, ordered by
    /// <see cref="PipelineTrigger.Order"/> — never re-synthesized alongside real rows. Otherwise
    /// exactly one synthetic trigger is materialized in-memory (never persisted,
    /// <see cref="PipelineTrigger.Id"/> left <see cref="Guid.Empty"/>) from <see cref="Pipeline.TriggerKind"/>.
    /// </summary>
    IReadOnlyList<PipelineTrigger> UpcastTriggers(Pipeline pipeline, Command? wrappingCommand);
}
