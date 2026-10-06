// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
