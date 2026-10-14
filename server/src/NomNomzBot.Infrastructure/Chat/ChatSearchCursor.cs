// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// The keyset position of the last chat row a search scanned: <c>(CreatedAt, Id)</c>, encoded as an opaque
/// base64url string so the client treats it as a token, not a format.
/// </summary>
internal readonly record struct ChatSearchCursor(DateTime CreatedAt, string Id)
{
    public string Encode() =>
        Base64Url.EncodeToString(
            Encoding.UTF8.GetBytes($"{CreatedAt.Ticks.ToString(CultureInfo.InvariantCulture)}|{Id}")
        );

    public static bool TryDecode(string? token, out ChatSearchCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrEmpty(token) || !Base64Url.IsValid(token))
            return false;

        string text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token));
        int separator = text.IndexOf('|');
        if (
            separator < 1
            || !long.TryParse(
                text.AsSpan(0, separator),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long ticks
            )
            || ticks > DateTime.MaxValue.Ticks
        )
            return false;

        cursor = new(new(ticks, DateTimeKind.Utc), text[(separator + 1)..]);
        return true;
    }
}
