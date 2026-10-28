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
