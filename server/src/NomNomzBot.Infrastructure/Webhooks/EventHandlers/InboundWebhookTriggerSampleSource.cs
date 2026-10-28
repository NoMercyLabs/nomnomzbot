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
