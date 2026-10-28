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

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// The named <see cref="HttpClient"/> the <c>AlejoPronounClient</c> fetches the
/// pronoun reference set through. The target is the fixed public alejo.io endpoint (not a
/// user-supplied URL), so it is a plain resilient client — distinct from the SSRF-hardened egress
/// client. The product User-Agent is applied globally (see <c>Platform/Http/AppUserAgent</c>), so
/// the registration only pins a timeout and the resilience handler.
/// </summary>
internal static class AlejoHttpClient
{
    public const string Name = "alejo-pronouns";
}
