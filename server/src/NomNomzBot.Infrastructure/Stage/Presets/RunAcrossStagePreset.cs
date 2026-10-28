// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Localization;

namespace NomNomzBot.Infrastructure.Stage.Presets;

/// <summary>The text or emoji runs from the left edge of the screen to the right edge (a train, a cat, a joke).</summary>
public sealed class RunAcrossStagePreset : IStagePreset
{
    public string Id => "run_across";

    public LocalizedText DisplayName => new("stage.preset.run_across.name");

    public int DefaultDurationMs => 6000;

    public StagePresetFields Uses =>
        StagePresetFields.Text | StagePresetFields.Intensity | StagePresetFields.Color;
}
