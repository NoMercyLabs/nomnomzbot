// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Application.DevPlatform.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Infrastructure.DevPlatform;

namespace NomNomzBot.Infrastructure.Tests.DevPlatform;

/// <summary>
/// Proves the reflected Event Catalog (dev-platform.md §1.2): it discovers every real domain event from the
/// Domain assembly, honours a class-level <c>[Event(...)]</c> override, derives a stable convention wire name
/// otherwise, orders the result, and fails fast on a duplicate wire name — the same startup guarantee the
/// automation registry gives.
/// </summary>
public sealed class EventCatalogTests
{
    [Fact]
    public void Discovers_the_full_domain_event_set_from_the_domain_assembly()
    {
        EventCatalog catalog = new();

        // The Domain assembly carries well over 100 event records; the catalog reflects all of them (not the 5
        // hand-written automation descriptors), and every wire name is unique.
        catalog.Descriptors.Should().HaveCountGreaterThan(100);
        catalog
            .Descriptors.Select(d => d.WireName)
            .Should()
            .OnlyHaveUniqueItems("a duplicate wire name would have failed construction");
    }

    [Fact]
    public void Honours_the_event_attribute_override_for_wire_name_and_tier()
    {
        EventCatalog catalog = new();

        EventDescriptor chat = catalog.Descriptors.Single(d =>
            d.ClrType == typeof(ChatMessageReceivedEvent)
        );
        chat.WireName.Should()
            .Be("chat.message", "the [Event(\"chat.message\", …)] override pins it");
        chat.Visibility.Should().Be(EventVisibility.Public);
    }

    [Fact]
    public void Defaults_an_unannotated_event_to_the_broadcaster_tier()
    {
        EventCatalog catalog = new();

        // RaidReceivedEvent carries no [Event] attribute → safe Broadcaster default, convention wire name.
        EventDescriptor raid = catalog.Descriptors.Single(d =>
            d.ClrType == typeof(RaidReceivedEvent)
        );
        raid.Visibility.Should().Be(EventVisibility.Broadcaster);
        raid.WireName.Should().Be("stream.raid.received");
    }

    [Theory]
    // module.<words> from the type name, trailing "Event" removed, PascalCase split, leading stutter dropped.
    [InlineData(typeof(RaidReceivedEvent), "stream.raid.received")]
    [InlineData(typeof(ChannelOnlineEvent), "stream.channel.online")]
    [InlineData(typeof(ChatMessageReceivedEvent), "chat.message.received")]
    public void Derives_the_convention_wire_name_from_the_type_name(Type type, string expected)
    {
        // DeriveWireName is the pure convention (it ignores any [Event] override), so it proves the rule even for
        // types that carry an explicit name in the live catalog.
        EventCatalog.DeriveWireName(type).Should().Be(expected);
    }

    [Fact]
    public void Fails_fast_on_a_duplicate_wire_name()
    {
        // Scanning the test assembly discovers the DupOne/DupTwo fixtures, which both claim "nnztest.dup".
        Action act = () => new EventCatalog(typeof(DupOneEvent).Assembly);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*nnztest.dup*", "the colliding wire name must be named in the failure");
    }

    [Fact]
    public void Orders_descriptors_by_wire_name()
    {
        EventCatalog catalog = new();

        catalog
            .Descriptors.Select(d => d.WireName)
            .Should()
            .BeInAscendingOrder(StringComparer.Ordinal);
    }
}
