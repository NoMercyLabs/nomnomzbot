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

using System.Text.RegularExpressions;

namespace NomNomzBot.Infrastructure.Tts.PipelineActions;

/// <summary>
/// Tells whether a TTS text template speaks the triggering viewer's own words. The viewer types those words
/// into a variable the trigger fills: a reward's <c>input</c>, a cheer's <c>message</c>, a command's
/// <c>args</c>. A line built from one of them is the viewer's speech, so it reads in the viewer's voice;
/// every other line is the bot's own and reads in the channel's one bot voice.
/// </summary>
internal static partial class ViewerWordsTemplate
{
    [GeneratedRegex(
        @"\{\{?\s*(input|message|args|user\.message)\s*\}\}?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex ViewerWordsVariable();

    public static bool IsIn(string template) => ViewerWordsVariable().IsMatch(template);
}
