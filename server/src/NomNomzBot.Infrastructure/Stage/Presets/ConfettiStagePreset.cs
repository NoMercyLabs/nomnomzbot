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

/// <summary>Colored paper pieces fall from the top of the screen.</summary>
public sealed class ConfettiStagePreset : IStagePreset
{
    public string Id => "confetti";

    public LocalizedText DisplayName => new("stage.preset.confetti.name");

    public int DefaultDurationMs => 4000;

    public StagePresetFields Uses => StagePresetFields.Intensity | StagePresetFields.Color;
}
