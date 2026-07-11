// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
