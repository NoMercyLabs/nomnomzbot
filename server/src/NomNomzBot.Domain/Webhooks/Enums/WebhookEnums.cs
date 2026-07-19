// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
