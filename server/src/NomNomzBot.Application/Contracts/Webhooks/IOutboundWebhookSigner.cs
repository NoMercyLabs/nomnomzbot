// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Webhooks;

/// <summary>The Standard Webhooks headers for one outbound delivery.</summary>
public sealed record WebhookSignatureHeaders(string WebhookId, string Timestamp, string Signature);

/// <summary>
/// Standard Webhooks signing (webhooks.md §3.8). In-box HMAC-SHA256, pure — secrets are supplied decrypted by the
/// caller. During rotation, signs with every active secret so the receiver accepts either during overlap.
/// </summary>
public interface IOutboundWebhookSigner
{
    /// <summary>
    /// Signs <c>"{id}.{timestamp}.{payload}"</c> with each active secret, producing a space-delimited
    /// <c>v1,&lt;base64&gt;</c> signature header plus the webhook-id / webhook-timestamp values.
    /// </summary>
    WebhookSignatureHeaders Sign(
        string webhookId,
        long timestampUnixSeconds,
        ReadOnlySpan<byte> payload,
        IReadOnlyList<byte[]> activeSecrets
    );
}
