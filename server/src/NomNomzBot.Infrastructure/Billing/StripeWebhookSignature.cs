// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;

namespace NomNomzBot.Infrastructure.Billing;

/// <summary>
/// Verifies a Stripe webhook signature (monetization-billing.md §5.2 / §9): recompute <c>HMAC-SHA256</c> over
/// <c>"{t}.{payload}"</c> with the webhook secret and compare to the header's <c>v1</c> scheme in constant time,
/// within a replay tolerance window. In-box crypto only — no Stripe SDK.
/// </summary>
public static class StripeWebhookSignature
{
    /// <summary>
    /// True iff <paramref name="signatureHeader"/> (the <c>Stripe-Signature</c> value, e.g. <c>t=...,v1=...</c>)
    /// authenticates <paramref name="payload"/> under <paramref name="secret"/> at <paramref name="nowUnix"/>
    /// within <paramref name="toleranceSeconds"/>.
    /// </summary>
    public static bool Verify(
        string payload,
        string? signatureHeader,
        string secret,
        long nowUnix,
        long toleranceSeconds = 300
    )
    {
        if (string.IsNullOrEmpty(signatureHeader) || string.IsNullOrEmpty(secret))
            return false;

        long? timestamp = null;
        List<string> candidates = [];
        foreach (string part in signatureHeader.Split(','))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0)
                continue;
            string key = part[..eq].Trim();
            string value = part[(eq + 1)..].Trim();
            if (key == "t" && long.TryParse(value, out long t))
                timestamp = t;
            else if (key == "v1")
                candidates.Add(value);
        }

        if (timestamp is not long ts || candidates.Count == 0)
            return false;
        if (Math.Abs(nowUnix - ts) > toleranceSeconds)
            return false; // outside the replay window

        byte[] expected = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{ts}.{payload}")
        );
        foreach (string candidate in candidates)
        {
            byte[] provided = FromHexOrEmpty(candidate);
            if (
                provided.Length == expected.Length
                && CryptographicOperations.FixedTimeEquals(provided, expected)
            )
                return true;
        }
        return false;
    }

    private static byte[] FromHexOrEmpty(string hex)
    {
        try
        {
            return Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return [];
        }
    }
}
