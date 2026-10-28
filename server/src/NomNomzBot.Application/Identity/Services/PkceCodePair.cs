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
        return new(verifier, challenge);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
