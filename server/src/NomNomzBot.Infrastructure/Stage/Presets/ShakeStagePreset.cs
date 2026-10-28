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

/// <summary>The whole stage layer jitters. It moves only the stage, never the stream underneath.</summary>
public sealed class ShakeStagePreset : IStagePreset
{
    public string Id => "shake";

    public LocalizedText DisplayName => new("stage.preset.shake.name");

    public int DefaultDurationMs => 1500;

    public StagePresetFields Uses => StagePresetFields.Intensity;
}
