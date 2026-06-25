// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
