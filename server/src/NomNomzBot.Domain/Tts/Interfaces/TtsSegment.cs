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
/// One spoken part of a multi-voice utterance: a provider renders an ordered list of these as ONE audio clip.
/// </summary>
/// <param name="Text">The words to speak.</param>
/// <param name="VoiceId">The resolved voice that speaks this part.</param>
/// <param name="RatePercent">Optional SSML-style speaking-rate override for this part; <c>null</c> keeps the default.</param>
/// <param name="PitchPercent">Optional SSML-style pitch override for this part; <c>null</c> keeps the default.</param>
/// <param name="BreakAfterMs">Silence after this part, in milliseconds; <c>0</c> adds none. Ignored after the last part.</param>
public sealed record TtsSegment(
    string Text,
    string VoiceId,
    double? RatePercent = null,
    double? PitchPercent = null,
    int BreakAfterMs = 0
);
