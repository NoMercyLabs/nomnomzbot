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

/// <summary>Colored paper pieces fall from the top of the screen.</summary>
public sealed class ConfettiStagePreset : IStagePreset
{
    public string Id => "confetti";

    public LocalizedText DisplayName => new("stage.preset.confetti.name");

    public int DefaultDurationMs => 4000;

    public StagePresetFields Uses => StagePresetFields.Intensity | StagePresetFields.Color;
}
