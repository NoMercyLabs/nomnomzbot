// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
