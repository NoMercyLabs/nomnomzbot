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

using System.Globalization;
using System.Security;
using System.Text;
using NomNomzBot.Domain.Tts.Interfaces;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// Builds the one SSML document Azure and Edge speak for a multi-voice utterance: a single <c>&lt;speak&gt;</c>
/// with one <c>&lt;voice&gt;</c> per segment and a <c>&lt;break&gt;</c> where a segment asks for trailing
/// silence. Every caller-supplied value is XML-escaped or clamped (see <see cref="TtsProsody"/>) before it
/// reaches the markup.
/// </summary>
internal static class TtsSegmentSsml
{
    internal static string Build(IReadOnlyList<TtsSegment> segments)
    {
        StringBuilder ssml = new();
        ssml.Append(
            "<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>"
        );
        for (int i = 0; i < segments.Count; i++)
        {
            TtsSegment segment = segments[i];
            ssml.Append("<voice name='")
                .Append(SecurityElement.Escape(segment.VoiceId))
                .Append("'><prosody rate='")
                .Append(TtsProsody.FormatPercent(segment.RatePercent))
                .Append("' pitch='")
                .Append(TtsProsody.FormatPercent(segment.PitchPercent))
                .Append("'>")
                .Append(SecurityElement.Escape(segment.Text))
                .Append("</prosody></voice>");

            bool isLast = i == segments.Count - 1;
            if (!isLast && segment.BreakAfterMs > 0)
                ssml.Append("<break time='")
                    .Append(
                        Math.Min(segment.BreakAfterMs, TtsProsody.MaxBreakMs)
                            .ToString(CultureInfo.InvariantCulture)
                    )
                    .Append("ms'/>");
        }
        return ssml.Append("</speak>").ToString();
    }
}
