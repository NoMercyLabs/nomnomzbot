// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Supporters.Dtos;

/// <summary>What kinds a provider emits and how it ingests (supporter-events.md §3).</summary>
public sealed record SupporterSourceCapabilities(
    IReadOnlyList<string> Kinds,
    string ConnectionMode,
    bool RequiresOAuth
);

/// <summary>
/// A normalized-but-not-yet-persisted supporter event, produced by an <c>ISupporterSource</c> from a raw
/// provider payload (supporter-events.md §3). The ingest service persists it and publishes the domain event.
/// </summary>
public sealed record SupporterEventDraft(
    string Kind,
    string SupporterDisplayName,
    long? AmountMinor,
    string? Currency,
    string? Tier,
    int? Quantity,
    string? ItemsJson,
    string? MessageText,
    bool IsRecurring,
    string ProviderTransactionId,
    string PayloadJson
);

/// <summary>Create/update a supporter connection (supporter-events.md §5).</summary>
public sealed record UpsertSupporterConnectionRequest(
    string SourceKey,
    string ConnectionMode,
    string? AuthSecret,
    Guid? IntegrationConnectionId,
    bool IsEnabled
);

/// <summary>
/// A supporter connection's public shape — the secret is never returned, only whether one is set. For a
/// webhook provider whose inbound endpoint was one-step provisioned from this page,
/// <paramref name="EndpointUrl"/> is the ingest URL to paste into the provider's webhook settings (null for
/// socket/ws/poll providers and for webhook connections without a provisioned endpoint).
/// </summary>
public sealed record SupporterConnectionDto(
    string SourceKey,
    string ConnectionMode,
    bool HasSecret,
    bool IsEnabled,
    string Status,
    DateTime? LastEventAt,
    string? EndpointUrl
);

/// <summary>One recorded supporter event for the events list (supporter-events.md §5).</summary>
public sealed record SupporterEventDto(
    Guid Id,
    string SourceKey,
    string Kind,
    string SupporterDisplayName,
    long? AmountMinor,
    string? Currency,
    string? Tier,
    int? Quantity,
    string? MessageText,
    bool IsRecurring,
    DateTime ReceivedAt
);

/// <summary>Filter/paging for the supporter events list.</summary>
public sealed record SupporterEventQuery(int Page, int PageSize, string? Kind, string? SourceKey);
