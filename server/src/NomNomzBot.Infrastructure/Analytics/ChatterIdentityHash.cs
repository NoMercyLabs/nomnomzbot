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

using System.Security.Cryptography;
using System.Text;

namespace NomNomzBot.Infrastructure.Analytics;

/// <summary>
/// The ONE hash for <c>ChannelChatterDays.ChatterHash</c>: SHA-256 hex (lowercase) of
/// <c>{provider}:{externalUserId}</c> — stable distinctness with no stored identity. Shared by the
/// analytics projection (which writes the anchor rows) and every consumer that must look a viewer's
/// rows back up (giveaway watch-time eligibility); a second implementation would silently diverge.
/// </summary>
public static class ChatterIdentityHash
{
    public static string Compute(string provider, string externalUserId) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes($"{provider}:{externalUserId}"))
        );
}
