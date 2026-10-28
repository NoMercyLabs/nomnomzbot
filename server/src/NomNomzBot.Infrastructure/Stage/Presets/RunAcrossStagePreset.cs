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
