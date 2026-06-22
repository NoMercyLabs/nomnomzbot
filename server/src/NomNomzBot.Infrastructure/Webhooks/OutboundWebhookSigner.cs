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
using NomNomzBot.Application.Contracts.Webhooks;

namespace NomNomzBot.Infrastructure.Webhooks;

/// <summary>
/// Standard Webhooks outbound signing (webhooks.md §3.8). In-box HMAC-SHA256 over <c>"{id}.{timestamp}.{payload}"</c>;
/// emits a <c>v1,&lt;base64&gt;</c> per active secret (primary + rotation secondary), space-delimited.
/// </summary>
public sealed class OutboundWebhookSigner : IOutboundWebhookSigner
{
    public WebhookSignatureHeaders Sign(
        string webhookId,
        long timestampUnixSeconds,
        ReadOnlySpan<byte> payload,
        IReadOnlyList<byte[]> activeSecrets
    )
    {
        byte[] prefix = Encoding.UTF8.GetBytes($"{webhookId}.{timestampUnixSeconds}.");
        byte[] signedContent = new byte[prefix.Length + payload.Length];
        prefix.CopyTo(signedContent, 0);
        payload.CopyTo(signedContent.AsSpan(prefix.Length));

        List<string> signatures = new(activeSecrets.Count);
        foreach (byte[] secret in activeSecrets)
            signatures.Add(
                "v1," + Convert.ToBase64String(HMACSHA256.HashData(secret, signedContent))
            );

        return new WebhookSignatureHeaders(
            webhookId,
            timestampUnixSeconds.ToString(),
            string.Join(' ', signatures)
        );
    }
}
