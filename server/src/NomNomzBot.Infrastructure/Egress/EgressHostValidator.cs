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

namespace NomNomzBot.Infrastructure.Egress;

/// <summary>
/// Decides whether a string may become an egress allowlist host. The allowlist is the SSRF boundary, so only a
/// public-looking DNS name passes: lowercase ASCII labels, at least two of them, no scheme, port, path, userinfo,
/// wildcard, IP literal, single-label name or local/internal suffix.
/// </summary>
internal static class EgressHostValidator
{
    private const int MaxHostLength = 253;
    private const int MaxLabelLength = 63;

    private static readonly string[] ReservedSuffixes = ["localhost", "local", "internal"];

    /// <summary>Normalise (trim, lowercase) and validate. Returns the host, or the reason it was refused.</summary>
    public static bool TryNormalize(string? raw, out string host, out string error)
    {
        host = (raw ?? string.Empty).Trim().ToLowerInvariant();
        error = string.Empty;

        if (host.Length == 0 || host.Length > MaxHostLength)
            return Fail("Host must be between 1 and 253 characters.", out error);
        if (IPAddress.TryParse(host, out _) || host.All(c => char.IsAsciiDigit(c) || c == '.'))
            return Fail("An IP address is not allowed; use the host name.", out error);

        string[] labels = host.Split('.');
        if (labels.Length < 2)
            return Fail("Host must be a full domain name such as hooks.example.com.", out error);
        if (ReservedSuffixes.Contains(labels[^1]))
            return Fail("Local and internal host names are not allowed.", out error);
        if (labels.Any(label => !IsValidLabel(label)))
            return Fail(
                "Host may only hold letters, digits and hyphens: no scheme, port, path, wildcard or spaces.",
                out error
            );
        return true;
    }

    private static bool IsValidLabel(string label) =>
        label.Length is >= 1 and <= MaxLabelLength
        && label[0] != '-'
        && label[^1] != '-'
        && label.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
