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

/// <summary>A color flash fills the screen and fades out.</summary>
public sealed class FlashStagePreset : IStagePreset
{
    public string Id => "flash";

    public LocalizedText DisplayName => new("stage.preset.flash.name");

    public int DefaultDurationMs => 800;

    public StagePresetFields Uses => StagePresetFields.Intensity | StagePresetFields.Color;
}
