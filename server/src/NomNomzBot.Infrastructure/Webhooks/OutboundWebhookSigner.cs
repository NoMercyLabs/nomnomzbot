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

        return new(webhookId, timestampUnixSeconds.ToString(), string.Join(' ', signatures));
    }
}
