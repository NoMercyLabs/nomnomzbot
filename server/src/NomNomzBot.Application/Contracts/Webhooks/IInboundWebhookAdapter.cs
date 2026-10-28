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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Domain.Webhooks.Enums;

namespace NomNomzBot.Application.Contracts.Webhooks;

/// <summary>
/// Per-provider inbound verify + parse (webhooks.md §3.3) — one impl per <see cref="WebhookAdapterKind"/>, the
/// seam that keeps provider quirks out of the dispatcher. Pure (no DB/IO); the dispatcher supplies the decrypted
/// secret.
/// </summary>
public interface IInboundWebhookAdapter
{
    WebhookAdapterKind Kind { get; }

    /// <summary>Verifies the request against the provider's own scheme using the decrypted secret (constant-time on MACs).</summary>
    Result<WebhookVerification> Verify(
        InboundWebhookRequest request,
        ReadOnlySpan<byte> secret,
        GenericInboundConfig? genericConfig
    );

    /// <summary>Parses the raw body into the normalized kind + dedupe id + flat variable bag.</summary>
    Result<ParsedInboundEvent> Parse(
        InboundWebhookRequest request,
        GenericInboundConfig? genericConfig
    );
}
