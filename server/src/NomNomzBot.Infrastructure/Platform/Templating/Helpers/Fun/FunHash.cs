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
