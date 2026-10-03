// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Webhooks.Enums;

namespace NomNomzBot.Infrastructure.Webhooks.EventHandlers;

/// <summary>The template variables an inbound webhook sets: <c>webhook.*</c> metadata and the tainted <c>payload.*</c> bag.</summary>
internal static class InboundWebhookVariables
{
    internal const string PayloadPrefix = "payload.";

    /// <summary>The keys the editor types for a webhook: the fixed metadata, and any payload key, since the payload is the endpoint's own.</summary>
    internal static IReadOnlyList<string> TypeKeys(string eventType, WebhookAdapterKind adapter) =>
        [.. Build(eventType, adapter, string.Empty, []).Keys, PayloadPrefix + "${string}"];

    internal static Dictionary<string, string> Build(
        string eventType,
        WebhookAdapterKind adapter,
        string providerEventId,
        IEnumerable<KeyValuePair<string, string>> payload
    )
    {
        Dictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase)
        {
            ["webhook.event_type"] = eventType,
            ["webhook.provider"] = adapter.ToString().ToLowerInvariant(),
            ["webhook.provider_event_id"] = providerEventId,
        };
        foreach ((string key, string value) in payload)
            variables[PayloadPrefix + key] = value;
        return variables;
    }
}
