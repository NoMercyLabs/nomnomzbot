// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
