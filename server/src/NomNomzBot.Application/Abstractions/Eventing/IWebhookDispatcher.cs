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

namespace NomNomzBot.Application.Abstractions.Eventing;

/// <summary>
/// Dispatches outbound webhook calls to external URLs.
/// Used for custom integrations and event forwarding.
/// </summary>
public interface IWebhookDispatcher
{
    /// <summary>Dispatch a payload to a webhook URL via HTTP POST.</summary>
    Task<WebhookDispatchResult> DispatchAsync(
        string webhookUrl,
        object payload,
        CancellationToken cancellationToken = default
    );

    /// <summary>Dispatch a payload with custom headers (e.g., HMAC signature).</summary>
    Task<WebhookDispatchResult> DispatchAsync(
        string webhookUrl,
        object payload,
        IDictionary<string, string> headers,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Result of a webhook dispatch attempt.</summary>
public sealed record WebhookDispatchResult(bool Success, int? StatusCode, string? ErrorMessage);
