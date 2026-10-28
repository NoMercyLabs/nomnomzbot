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

using System.Text.Json;
using System.Text.Json.Serialization;

namespace NomNomzBot.Application.Abstractions.Pipeline;

public sealed class PipelineStepDefinition
{
    [JsonPropertyName("condition")]
    public ConditionDefinition? Condition { get; set; }

    [JsonPropertyName("action")]
    public required ActionDefinition Action { get; set; }

    [JsonPropertyName("stop_on_match")]
    public bool StopOnMatch { get; set; }

    /// <summary>When <c>true</c>, a failed action does not abort the pipeline — the engine
    /// continues to the next step. Defaults to <c>false</c> (fail-closed).</summary>
    [JsonPropertyName("continue_on_error")]
    public bool ContinueOnError { get; set; }

    // ── Tree-nesting fields (S046-branching-prereq) — null for a flat/top-level step, matching
    // today's shape exactly. Mirror <see cref="Domain.Commands.Entities.PipelineStep"/>'s
    // ParentStepId/Branch/BlockKind/BlockConfigJson/Order so the wire graph can round-trip a nested
    // pipeline; the tree walker itself already reads/writes those DB columns directly and is
    // untouched by this DTO.

    /// <summary>This step's own id in the graph, referenced by a child's <see cref="ParentStepId"/>.
    /// Null for a step the client has not yet round-tripped through a save (brand new).</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The <see cref="Id"/> of this step's parent block step; null for a top-level step.</summary>
    [JsonPropertyName("parent_step_id")]
    public string? ParentStepId { get; set; }

    /// <summary>Branch lane under the parent block: "then" | "else" | null.</summary>
    [JsonPropertyName("branch")]
    public string? Branch { get; set; }

    /// <summary>Block kind when this step is a block (if/switch/loop/...); null for a leaf action step.</summary>
    [JsonPropertyName("block_kind")]
    public string? BlockKind { get; set; }

    /// <summary>Block-kind-specific configuration; null for a leaf step or a parameterless block kind.</summary>
    [JsonPropertyName("block_config")]
    public JsonElement? BlockConfig { get; set; }

    /// <summary>Execution order within this step's (parent, branch) group; null lets the caller fall
    /// back to the step's position in the flat array (today's behavior for un-nested pipelines).</summary>
    [JsonPropertyName("order")]
    public int? Order { get; set; }
}
