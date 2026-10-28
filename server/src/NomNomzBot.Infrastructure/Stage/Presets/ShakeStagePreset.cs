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

/// <summary>The whole stage layer jitters. It moves only the stage, never the stream underneath.</summary>
public sealed class ShakeStagePreset : IStagePreset
{
    public string Id => "shake";

    public LocalizedText DisplayName => new("stage.preset.shake.name");

    public int DefaultDurationMs => 1500;

    public StagePresetFields Uses => StagePresetFields.Intensity;
}
