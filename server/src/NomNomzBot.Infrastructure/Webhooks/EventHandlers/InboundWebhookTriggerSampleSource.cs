// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.Webhooks.Enums;

namespace NomNomzBot.Infrastructure.Webhooks.EventHandlers;

/// <summary>A test-run sample of an inbound webhook, built by the code the live bridge uses.</summary>
public sealed class InboundWebhookTriggerSampleSource : ITriggerSampleSource
{
    /// <summary>The key a webhook endpoint's own pipeline is typed and sampled under.</summary>
    public const string ResponseKey = "webhook";

    private const string EventType = "webhook.generic.order";

    public TriggerSample Sample(DateTimeOffset now) =>
        new(
            ResponseKey,
            ResponseKey,
            "webhook",
            "Example endpoint",
            InboundWebhookVariables.Build(
                EventType,
                WebhookAdapterKind.Generic,
                "evt_1001",
                new Dictionary<string, string> { ["order_id"] = "1001", ["amount"] = "25.00" }
            )
        )
        {
            TypeKeys = InboundWebhookVariables.TypeKeys(EventType, WebhookAdapterKind.Generic),
        };
}
