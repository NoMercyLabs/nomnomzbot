// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

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
}
