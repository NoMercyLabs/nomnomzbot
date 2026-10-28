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

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// The content classification label ids Twitch lets a broadcaster switch (Modify Channel Information,
/// <c>content_classification_labels[].id</c>). <c>MatureGame</c> is not on the list: Twitch sets it from the
/// category, and asking for a gaming label returns 403.
/// </summary>
public static class TwitchContentLabels
{
    public static readonly IReadOnlyList<string> Editable =
    [
        "DebatedSocialIssuesAndPolitics",
        "DrugsIntoxication",
        "SexualThemes",
        "ViolentGraphic",
        "Gambling",
        "ProfanityVulgarity",
    ];
}
