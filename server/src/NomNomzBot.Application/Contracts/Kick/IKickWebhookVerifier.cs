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

namespace NomNomzBot.Application.Contracts.Kick;

/// <summary>
/// Verifies a Kick webhook delivery's authenticity (docs.kick.com webhook-security, verified
/// 2026-07-11): Kick signs <c>{Kick-Event-Message-Id}.{Kick-Event-Message-Timestamp}.{raw body}</c>
/// with its private RSA key (SHA-256, PKCS#1 v1.5) and sends the Base64 signature in
/// <c>Kick-Event-Signature</c>; the public key comes from <c>GET /public/v1/public-key</c>. Fails
/// CLOSED: an unfetchable key, malformed signature, or mismatch is <c>false</c> — never a throw into
/// the ingest path.
/// </summary>
public interface IKickWebhookVerifier
{
    Task<bool> VerifyAsync(
        string messageId,
        string timestamp,
        string rawBody,
        string signatureBase64,
        CancellationToken cancellationToken = default
    );
}
