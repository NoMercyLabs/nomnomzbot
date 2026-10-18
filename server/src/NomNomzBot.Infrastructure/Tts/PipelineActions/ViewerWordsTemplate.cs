// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
