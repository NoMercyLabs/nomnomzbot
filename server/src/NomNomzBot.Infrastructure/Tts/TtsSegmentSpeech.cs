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
using Humanizer;
using NomNomzBot.Domain.Tts.Interfaces;

namespace NomNomzBot.Infrastructure.Tts;

internal static class TtsSegmentSpeech
{
    private static readonly IReadOnlyDictionary<
        string,
        (double Rate, double Pitch, double Volume)
    > StyleSteps = new Dictionary<string, (double, double, double)>(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["cheerful"] = (8, 8, 10),
        ["excited"] = (15, 12, 18),
        ["sad"] = (-12, -8, -8),
        ["whispering"] = (-18, -5, -30),
        ["shouting"] = (8, 5, 30),
        ["friendly"] = (3, 4, 5),
        ["unfriendly"] = (-5, -5, 5),
        ["terrified"] = (16, 14, 12),
        ["hopeful"] = (5, 7, 7),
    };

    internal static TtsSegment Approximate(TtsSegment segment)
    {
        if (
            segment.Style is null
            || !StyleSteps.TryGetValue(
                segment.Style,
                out (double Rate, double Pitch, double Volume) step
            )
        )
            return segment;

        return segment with
        {
            RatePercent = (segment.RatePercent ?? 0) + step.Rate,
            PitchPercent = (segment.PitchPercent ?? 0) + step.Pitch,
            VolumePercent = (segment.VolumePercent ?? 0) + step.Volume,
        };
    }

    internal static string PlainText(TtsSegment segment)
    {
        string text = segment.Text;
        if (segment.SayAs is null || !TtsSpeechOptions.SayAsValues.Contains(segment.SayAs))
            return text;

        CultureInfo culture = segment.VoiceId.StartsWith("nl-", StringComparison.OrdinalIgnoreCase)
            ? CultureInfo.GetCultureInfo("nl-NL")
            : CultureInfo.GetCultureInfo("en-US");
        switch (segment.SayAs.ToLowerInvariant())
        {
            case "spell-out":
                return string.Join(' ', text.Where(character => !char.IsWhiteSpace(character)));
            case "digits":
            case "telephone":
                return Digits(
                    text,
                    culture,
                    segment.SayAs.Equals("telephone", StringComparison.OrdinalIgnoreCase)
                );
            case "cardinal":
            case "ordinal":
                if (TryNumber(text, out int number))
                    return segment.SayAs.Equals("ordinal", StringComparison.OrdinalIgnoreCase)
                        ? number.ToOrdinalWords(culture)
                        : number.ToWords(culture);
                return text;
            case "date":
                if (DateTime.TryParse(text, culture, DateTimeStyles.None, out DateTime date))
                    return $"{date.Day.ToOrdinalWords(culture)} {date.ToString("MMMM", culture)} {date.Year.ToWords(culture)}";
                return text;
            case "time":
                if (TimeOnly.TryParse(text, culture, DateTimeStyles.None, out TimeOnly time))
                    return time.Minute == 0
                        ? time.Hour.ToWords(culture)
                        : $"{time.Hour.ToWords(culture)} {time.Minute.ToWords(culture)}";
                return text;
            default:
                return text;
        }
    }

    private static bool TryNumber(string text, out int number)
    {
        string trimmed = text.Trim();
        if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out number))
        {
            string withoutSuffix = trimmed.TrimEnd('s', 't', 'n', 'd', 'r', 'h', 'e');
            if (
                !int.TryParse(
                    withoutSuffix,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out number
                )
            )
                return false;
        }
        return number is >= 0 and <= 9999;
    }

    private static string Digits(string text, CultureInfo culture, bool telephone)
    {
        List<string> spoken = [];
        foreach (char character in text)
        {
            if (char.IsAsciiDigit(character))
                spoken.Add((character - '0').ToWords(culture));
            else if (telephone && character == '+')
                spoken.Add("plus");
            else if (!telephone && !char.IsWhiteSpace(character))
                spoken.Add(character.ToString());
        }
        return spoken.Count == 0 ? text : string.Join(' ', spoken);
    }
}
