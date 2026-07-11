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
