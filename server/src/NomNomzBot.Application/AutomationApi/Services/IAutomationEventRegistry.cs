// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
