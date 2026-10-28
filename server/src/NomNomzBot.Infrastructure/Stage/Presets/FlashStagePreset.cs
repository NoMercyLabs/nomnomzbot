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

/// <summary>A color flash fills the screen and fades out.</summary>
public sealed class FlashStagePreset : IStagePreset
{
    public string Id => "flash";

    public LocalizedText DisplayName => new("stage.preset.flash.name");

    public int DefaultDurationMs => 800;

    public StagePresetFields Uses => StagePresetFields.Intensity | StagePresetFields.Color;
}
