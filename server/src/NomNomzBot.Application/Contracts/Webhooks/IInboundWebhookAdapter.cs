// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
