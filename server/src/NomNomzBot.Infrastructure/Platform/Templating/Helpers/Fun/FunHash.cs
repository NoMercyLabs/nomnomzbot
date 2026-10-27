// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Fun;

/// <summary>
/// The stable numbers behind the rating rows: the same inputs on the same day (in the channel's zone) always give
/// the same number, and another day gives another one.
/// </summary>
internal static class FunHash
{
    private const char Separator = '\u001F';

    /// <summary>A number from 0 up to, not including, <paramref name="exclusiveMax"/>.</summary>
    public static int Below(TemplateRenderScope scope, int exclusiveMax, params string[] parts)
    {
        string text =
            string.Join(Separator, parts.Select(p => p.ToLowerInvariant()))
            + Separator
            + Day(scope);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return (int)(BitConverter.ToUInt32(hash, 0) % (uint)exclusiveMax);
    }

    private static string Day(TemplateRenderScope scope) =>
        TimeZoneInfo
            .ConvertTime(scope.Now, StreamText.ZoneOf(scope.Timezone))
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
