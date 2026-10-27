// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;

/// <summary>Encodings for links and transport: URL parts, base64 and HTML tags. Text that does not decode is a bad argument.</summary>
internal sealed partial class EncodingTextHelpers()
    : TextHelperGroup(
        TextHelperRow.OfText("text.url.encode", "a%20b%26c", Uri.EscapeDataString),
        TextHelperRow.OfText("text.url.decode", "a b", Uri.UnescapeDataString),
        TextHelperRow.OfText("text.query.encode", "a+b", WebUtility.UrlEncode),
        TextHelperRow.OfText("text.path.encode", "a%2Fb%20c", Uri.EscapeDataString),
        TextHelperRow.OfText(
            "text.base64.encode",
            "aGk=",
            text => Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
        ),
        TextHelperRow.OfText("text.base64.decode", "hi", FromBase64),
        TextHelperRow.OfText("text.html.strip", "hi", StripTags)
    )
{
    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tag();

    private static string? FromBase64(string text)
    {
        byte[] bytes = new byte[text.Length];
        return Convert.TryFromBase64String(text, bytes, out int written)
            ? Encoding.UTF8.GetString(bytes, 0, written)
            : null;
    }

    private static string StripTags(string text) =>
        WebUtility.HtmlDecode(Tag().Replace(text, string.Empty));
}
