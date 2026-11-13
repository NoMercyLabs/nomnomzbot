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

using System.Text;

namespace NomNomzBot.Infrastructure.Platform.Transport.LivePlaylist;

/// <summary>
/// The player's proxy form of a playlist address (base64url, no padding) and the host allowlist: https on the
/// default port, no user info, and a host under <c>ttvnw.net</c> (usher and the video-weaver hosts). A look-alike
/// such as <c>ttvnw.net.evil.example</c> is not under it, so the proxy can never fetch another site.
/// </summary>
internal static class TwitchLivePlaylistAddress
{
    private const string TwitchVideoDomain = ".ttvnw.net";

    public static Uri? Decode(string encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return null;

        string base64 = encoded.Replace('-', '+').Replace('_', '/');
        string padded = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        try
        {
            string text = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            return Uri.TryCreate(text, UriKind.Absolute, out Uri? address) && IsAllowed(address)
                ? address
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static string Encode(Uri address)
    {
        string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(address.AbsoluteUri));
        return base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool IsAllowed(Uri address) =>
        address.Scheme == Uri.UriSchemeHttps
        && address is { IsDefaultPort: true, UserInfo.Length: 0 }
        && address.Host.EndsWith(TwitchVideoDomain, StringComparison.OrdinalIgnoreCase);
}
