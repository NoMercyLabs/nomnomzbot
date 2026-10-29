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

using System.Text.RegularExpressions;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// What the bot sends as Edge's read-aloud client, derived from <see cref="EdgeTtsOptions"/>. One Chromium
/// version feeds both Sec-MS-GEC-Version and the User-Agent, because Microsoft refuses the handshake when the
/// two disagree. A malformed setting keeps the shipped value, so a typo cannot silence every voice.
/// </summary>
public sealed partial class EdgeClientIdentity
{
    private EdgeClientIdentity(string chromiumVersion, string trustedClientToken)
    {
        ChromiumVersion = chromiumVersion;
        TrustedClientToken = trustedClientToken;
        string major = chromiumVersion.Split('.')[0];
        SecMsGecVersion = $"{EdgeSecMsGecToken.Version}-{chromiumVersion}";
        UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
            + $"Chrome/{major}.0.0.0 Safari/537.36 Edg/{major}.0.0.0";
    }

    public string ChromiumVersion { get; }
    public string TrustedClientToken { get; }
    public string SecMsGecVersion { get; }
    public string UserAgent { get; }

    /// <summary>
    /// The identity for these settings. <paramref name="rejected"/> names each setting that was set but
    /// malformed, and so was replaced by its shipped default.
    /// </summary>
    public static EdgeClientIdentity Resolve(EdgeTtsOptions options, out List<string> rejected)
    {
        rejected = [];
        string version = Pick(
            options.ChromiumVersion,
            EdgeTtsOptions.DefaultChromiumVersion,
            ChromiumVersionShape(),
            nameof(EdgeTtsOptions.ChromiumVersion),
            rejected
        );
        string token = Pick(
            options.TrustedClientToken,
            EdgeTtsOptions.DefaultTrustedClientToken,
            TrustedClientTokenShape(),
            nameof(EdgeTtsOptions.TrustedClientToken),
            rejected
        );
        return new(version, token);
    }

    private static string Pick(
        string? configured,
        string fallback,
        Regex shape,
        string key,
        List<string> rejected
    )
    {
        string value = configured?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return fallback;
        if (shape.IsMatch(value))
            return value;
        rejected.Add(key);
        return fallback;
    }

    [GeneratedRegex(@"^\d{1,4}\.\d{1,4}\.\d{1,5}\.\d{1,5}$", RegexOptions.None, 200)]
    private static partial Regex ChromiumVersionShape();

    [GeneratedRegex("^[0-9A-Fa-f]{32}$", RegexOptions.None, 200)]
    private static partial Regex TrustedClientTokenShape();
}
