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

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// Shared clamp/format rules for the per-call SSML <c>&lt;prosody&gt;</c> rate/pitch overrides that
/// <see cref="EdgeTtsProvider"/> and <see cref="AzureTtsProvider"/> both accept. A caller-supplied value (e.g.
/// from a code script) must never reach the outbound SSML unclamped — Microsoft's synthesis services reject
/// or misbehave on nonsense like <c>rate='+99999%'</c> — so every value is clamped to a safe range before
/// formatting. This is not an injection concern (the numeric formatting below can never emit markup); it is
/// purely about not sending the provider a value it can't handle.
/// </summary>
internal static class TtsProsody
{
    internal const double MinPercent = -50;
    internal const double MaxPercent = 50;

    /// <summary>The longest silence one <c>&lt;break&gt;</c> may ask for (Azure/Edge reject more than 5 s).</summary>
    internal const int MaxBreakMs = 5000;

    /// <summary>
    /// Formats an optional percent override as SSML's <c>+N%</c>/<c>-N%</c> token, clamped to
    /// <see cref="MinPercent"/>..<see cref="MaxPercent"/>. <c>null</c> formats as <c>+0%</c> (provider default —
    /// today's unchanged behavior).
    /// </summary>
    internal static string FormatPercent(double? percent)
    {
        double clamped = Math.Clamp(percent ?? 0, MinPercent, MaxPercent);
        string sign = clamped >= 0 ? "+" : string.Empty;
        return $"{sign}{clamped.ToString("0.##", CultureInfo.InvariantCulture)}%";
    }
}
