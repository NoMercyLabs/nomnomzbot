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

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// The names a template variable bag holds (the triggering user and the target), as the
/// <c>SpokenNames</c> list of a TTS request.
/// </summary>
internal static class SpokenNameVariables
{
    private static readonly string[] NameKeys =
    [
        "user",
        "user.name",
        "user.displayname",
        "user.login",
        "target",
        "target.name",
        "target.displayname",
    ];

    public static List<string> From(
        IReadOnlyDictionary<string, string> variables,
        params string?[] extra
    )
    {
        List<string> names = [];
        foreach (string key in NameKeys)
            if (variables.TryGetValue(key, out string? value))
                names.Add(value);
        names.AddRange(extra.OfType<string>());
        return names.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
    }
}
