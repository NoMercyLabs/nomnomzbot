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

using System.Diagnostics.CodeAnalysis;
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Application.AutomationApi.Services;

/// <summary>
/// One descriptor per externally-exposed domain event (automation-api.md §3/D6). The public event
/// surface is default-deny: an event without a descriptor is invisible to automation clients.
/// Exposing a new event = drop a descriptor implementation — no engine edit.
/// </summary>
public interface IAutomationEventDescriptor
{
    /// <summary>Stable wire name, e.g. <c>Twitch.ChatMessage</c>, <c>Supporter.Received</c>.</summary>
    string PublicName { get; }

    /// <summary>What the wire name means, for the integrator-facing catalog.</summary>
    string Description { get; }

    /// <summary>The source <see cref="DomainEventBase"/> subtype this descriptor exposes.</summary>
    Type DomainEventType { get; }

    /// <summary>The PII-safe public projection — never the raw domain event.</summary>
    object ProjectPayload(DomainEventBase domainEvent);
}

/// <summary>The auto-discovered catalog of public automation events (automation-api.md §3/D6).</summary>
public interface IAutomationEventRegistry
{
    bool TryGet(
        Type domainEventType,
        [MaybeNullWhen(false)] out IAutomationEventDescriptor descriptor
    );

    IReadOnlyList<AutomationEventCatalogItem> Catalog { get; }
}
