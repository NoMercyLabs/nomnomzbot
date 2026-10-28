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

using System.Text.Json.Serialization;

namespace NomNomzBot.Domain.Webhooks.Enums;

/// <summary>
/// The inbound provider an endpoint speaks (webhooks.md §2). Serialized as its string name on the API wire
/// (not an ordinal) so the dashboard sends and reads a stable adapter key (e.g. <c>"Generic"</c>) rather than a
/// brittle integer, and the OpenAPI schema advertises the real choice set.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WebhookAdapterKind
{
    Kofi,
    Github,
    Generic,
    Fourthwall,
    Shopify,
    Patreon,
    Buymeacoffee,
}

/// <summary>The lifecycle of one outbound delivery attempt.</summary>
public enum WebhookDeliveryStatus
{
    Pending,
    Delivered,
    Failed,
    DeadLetter,
}

/// <summary>Why an inbound webhook was rejected before any side effect.</summary>
public enum WebhookRejectReason
{
    InvalidSignature,
    ReplayWindow,
    Disabled,
    PayloadTooLarge,
    UnsupportedMediaType,
    RateLimited,
    Malformed,
    UnknownEndpoint,
}
