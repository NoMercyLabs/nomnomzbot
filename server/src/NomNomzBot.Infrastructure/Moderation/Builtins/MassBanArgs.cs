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

namespace NomNomzBot.Infrastructure.Moderation.Builtins;

/// <summary>The one argument <c>!allow</c> and <c>!disallow</c> understand today.</summary>
internal static class MassBanArgs
{
    public static bool IsMassBan(string args) =>
        string.Equals(args.Trim(), "massban", StringComparison.OrdinalIgnoreCase);
}
