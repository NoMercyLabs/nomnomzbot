// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using FluentAssertions;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Infrastructure.Overlays;
using NomNomzBot.Infrastructure.Webhooks;

namespace NomNomzBot.Infrastructure.Tests.Webhooks;

/// <summary>
/// Guards the outbound webhook event catalogue (webhooks.md §9): every entry is a REAL journaled domain event (so a
/// subscription can actually match at fan-out), the catalogue never contains a §9 deny-listed webhook-lifecycle type,
/// no entry is internal platform plumbing, and the catalogue reuses (covers) <see cref="OverlayEventFilter"/>'s
/// curated user-facing roster rather than drifting into a parallel list.
/// </summary>
public sealed class OutboundWebhookEventCatalogueTests
{
    private static readonly IReadOnlySet<string> DomainEventTypeNames = typeof(DomainEventBase)
        .Assembly.GetTypes()
        .Where(t => !t.IsAbstract && typeof(DomainEventBase).IsAssignableFrom(t))
        .Select(t => t.Name)
        .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Every_catalogue_entry_is_a_real_journaled_domain_event()
    {
        // The fan-out matches EventRecord.EventType == typeof(TEvent).Name, so a catalogue entry that is not a real
        // DomainEventBase type (e.g. an entity mistaken for an event) could never be delivered — a silent dead option.
        foreach (string eventType in OutboundWebhookEventCatalogue.Entries.Select(e => e.EventType))
            DomainEventTypeNames
                .Should()
                .Contain(eventType, "catalogue entry '{0}' must be a real domain event", eventType);
    }

    [Fact]
    public void Catalogue_is_disjoint_from_the_lifecycle_deny_list()
    {
        foreach (string eventType in OutboundWebhookEventCatalogue.Entries.Select(e => e.EventType))
            OutboundWebhookEventCatalogue.IsLifecycle(eventType).Should().BeFalse();
    }

    [Fact]
    public void No_catalogue_entry_is_internal_platform_plumbing()
    {
        foreach (string eventType in OutboundWebhookEventCatalogue.Entries.Select(e => e.EventType))
            OverlayEventFilter.IsInternalPlumbing(eventType).Should().BeFalse();
    }

    [Fact]
    public void Catalogue_covers_the_overlay_user_facing_business_roster()
    {
        // Reuse, not parallel: OverlayEventFilter's curated user-facing set is the seed — the webhook catalogue must
        // offer at least everything on it.
        IReadOnlySet<string> catalogue = OutboundWebhookEventCatalogue
            .Entries.Select(e => e.EventType)
            .ToHashSet(StringComparer.Ordinal);

        foreach (string userFacing in OverlayEventFilter.UserFacingBusinessEvents)
            catalogue
                .Should()
                .Contain(
                    userFacing,
                    "the webhook catalogue must offer every overlay user-facing event"
                );
    }

    [Fact]
    public void Lifecycle_deny_list_types_are_never_subscribable()
    {
        foreach (string eventType in OutboundWebhookEventCatalogue.LifecycleDenyList)
        {
            OutboundWebhookEventCatalogue.IsLifecycle(eventType).Should().BeTrue();
            OutboundWebhookEventCatalogue.IsSubscribable(eventType).Should().BeFalse();
        }
    }

    [Fact]
    public void Catalogue_event_types_are_unique()
    {
        IEnumerable<string> types = OutboundWebhookEventCatalogue.Entries.Select(e => e.EventType);
        types.Should().OnlyHaveUniqueItems();
    }
}
