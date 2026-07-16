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

namespace NomNomzBot.Infrastructure.Supporters;

/// <summary>
/// The single source of truth pairing each webhook-mode supporter source with its inbound-webhook adapter
/// kind. The bridge reads it kind→source (routing a verified webhook into ingest); one-step endpoint
/// provisioning reads it source→kind (creating the right endpoint from the Supporters page). A webhook
/// provider missing here is a wiring bug both directions would surface.
/// </summary>
internal static class SupporterWebhookAdapters
{
    private static readonly IReadOnlyDictionary<WebhookAdapterKind, string> SourceByKind =
        new Dictionary<WebhookAdapterKind, string>
        {
            [WebhookAdapterKind.Kofi] = "kofi",
            [WebhookAdapterKind.Fourthwall] = "fourthwall",
            [WebhookAdapterKind.Shopify] = "shopify",
            [WebhookAdapterKind.Patreon] = "patreon",
            [WebhookAdapterKind.Buymeacoffee] = "buymeacoffee",
        };

    private static readonly IReadOnlyDictionary<string, WebhookAdapterKind> KindBySource =
        SourceByKind.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>The supporter source a webhook adapter feeds, or null for non-monetization adapters.</summary>
    public static string? SourceFor(WebhookAdapterKind adapter) =>
        SourceByKind.GetValueOrDefault(adapter);

    /// <summary>The inbound adapter kind for a webhook-mode supporter source, or null when it has none.</summary>
    public static WebhookAdapterKind? AdapterFor(string sourceKey) =>
        KindBySource.TryGetValue(sourceKey, out WebhookAdapterKind kind) ? kind : null;
}
