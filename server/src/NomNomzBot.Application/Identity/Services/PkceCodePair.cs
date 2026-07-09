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

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// A PKCE (RFC 7636) verifier + S256 challenge pair for auth-code login providers. The verifier is held in the
/// server-side OAuth state; only the challenge travels to the provider on the authorize URL, and the verifier
/// proves at the token exchange that the same client finished the flow. The verifier is 32 bytes of high
/// entropy, base64url-encoded (43 chars, within the RFC's 43–128 range).
/// </summary>
public readonly record struct PkceCodePair(string Verifier, string Challenge)
{
    public static PkceCodePair Generate()
    {
        string verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return new PkceCodePair(verifier, challenge);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
