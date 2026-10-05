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

namespace NomNomzBot.Infrastructure.Rewards.EventHandlers;

/// <summary>
/// Removes cheermotes ("Cheer100", "uni1") from a cheer message before it becomes a template variable, so a
/// pipeline that speaks <c>{message}</c> never reads them aloud. Same pattern as the old bot's StripCheermotes.
/// </summary>
internal static partial class CheermoteText
{
    [GeneratedRegex(@"\b[A-Za-z]+\d+\b")]
    private static partial Regex CheermotePattern();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex RepeatedSpaces();

    public static string Strip(string message) =>
        RepeatedSpaces().Replace(CheermotePattern().Replace(message, string.Empty), " ").Trim();
}
