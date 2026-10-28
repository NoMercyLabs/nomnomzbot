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
