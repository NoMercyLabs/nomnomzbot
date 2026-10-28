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

namespace NomNomzBot.Infrastructure.Stage;

/// <summary>
/// One one-off effect the stage overlay can play. A preset is a small registered class: a new effect is a new
/// class here plus its animation in <c>stage.vue</c>, never a new route and never a growing switch. The server
/// side only describes the preset (who it is, how long it runs, which payload fields it reads); the widget plays it.
/// </summary>
public interface IStagePreset
{
    /// <summary>The wire id the pipeline action and the widget share, for example <c>run_across</c>.</summary>
    string Id { get; }

    /// <summary>Translation key of the name shown in pickers.</summary>
    LocalizedText DisplayName { get; }

    /// <summary>How long the effect runs, in milliseconds, when the caller sets no duration.</summary>
    int DefaultDurationMs { get; }

    /// <summary>The optional payload fields this preset reads.</summary>
    StagePresetFields Uses { get; }
}
