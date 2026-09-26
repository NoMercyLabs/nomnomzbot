// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Content.PlatformContent.Templates;

/// <summary>
/// Install is a copy, never an overwrite of the channel's own work: a template whose name is already taken in
/// the channel is installed under the first free "Name 2", "Name 3", … instead.
/// </summary>
internal static class PlatformTemplateNames
{
    private const int MaxAttempts = 100;

    public static async Task<string?> FreeNameAsync(
        string baseName,
        int maxLength,
        Func<string, Task<bool>> isTaken
    )
    {
        string trimmed = baseName.Trim();
        if (!await isTaken(trimmed))
            return trimmed;

        for (int n = 2; n <= MaxAttempts; n++)
        {
            string suffix = $" {n}";
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
