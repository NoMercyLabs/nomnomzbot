// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
