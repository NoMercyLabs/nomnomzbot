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

namespace NomNomzBot.Domain.Tts.Interfaces;

/// <summary>
/// One block of a spoken line: a provider renders an ordered list of these as ONE audio clip, each provider by
/// its own rules. Blocks combine in any order. Blocks of one voice that follow each other with no pause between
/// them are one flowing sentence, so a single stressed word is its own block between two plain ones.
/// </summary>
/// <param name="Text">The words to speak.</param>
/// <param name="VoiceId">The resolved voice that speaks this part.</param>
/// <param name="RatePercent">Optional SSML-style speaking-rate override for this part; <c>null</c> keeps the default.</param>
/// <param name="PitchPercent">Optional SSML-style pitch override for this part; <c>null</c> keeps the default.</param>
/// <param name="BreakAfterMs">Silence after this part, in milliseconds; <c>0</c> adds none. Ignored after the last part.</param>
/// <param name="VolumePercent">Optional loudness change for this part, in percent; <c>null</c> keeps the default.</param>
/// <param name="Stressed">
/// These words carry the stress. The caller says which block; each provider decides how stress sounds, and a
/// provider that cannot stress speaks the block plain.
/// </param>
public sealed record TtsSegment(
    string Text,
    string VoiceId,
    double? RatePercent = null,
    double? PitchPercent = null,
    int BreakAfterMs = 0,
    double? VolumePercent = null,
    bool Stressed = false
);
