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

namespace NomNomzBot.Infrastructure.Content.PlatformContent.Templates;

/// <summary>
/// Install is a copy, never an overwrite of the channel's own work: a template whose name is already taken in
/// the channel is installed under the first free "Name 2", "Name 3", … instead. A name that is a slug passes
/// <c>separator: "-"</c> and gets "name-2", "name-3", ….
/// </summary>
internal static class PlatformTemplateNames
{
    private const int MaxAttempts = 100;

    public static async Task<string?> FreeNameAsync(
        string baseName,
        int maxLength,
        Func<string, Task<bool>> isTaken,
        string separator = " "
    )
    {
        string trimmed = baseName.Trim();
        if (!await isTaken(trimmed))
            return trimmed;

        for (int n = 2; n <= MaxAttempts; n++)
        {
            string suffix = $"{separator}{n}";
            string head =
                trimmed.Length + suffix.Length > maxLength
                    ? trimmed[..(maxLength - suffix.Length)]
                    : trimmed;
            string candidate = head + suffix;
            if (!await isTaken(candidate))
                return candidate;
        }

        return null;
    }
}
