// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.AutomationApi.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Supporters.Events;
using NomNomzBot.Infrastructure.AutomationApi.Events;

namespace NomNomzBot.Infrastructure.Tests.AutomationApi;

/// <summary>
/// Proves the public event catalog is default-deny and PII-safe (automation-api.md D6): the registry
/// resolves a descriptor by domain-event type and reports nothing for an undescribed event; the
/// catalog lists every registered wire name (sorted, stable); a duplicate wire name or a double claim
/// on one event type fails fast at construction; and the supporter projection exposes ONLY the public
/// display fields — the internal row id, supporter user id, and source key never reach the wire.
/// </summary>
public sealed class AutomationEventRegistryTests
{
    private static AutomationEventRegistry BuildFull() =>
        new([
            new ChatMessageEventDescriptor(),
            new StreamOnlineEventDescriptor(),
            new StreamOfflineEventDescriptor(),
            new RaidReceivedEventDescriptor(),
            new SupporterReceivedEventDescriptor(),
        ]);

    [Fact]
    public void Resolves_described_events_and_denies_everything_else()
    {
        AutomationEventRegistry registry = BuildFull();

        registry
            .TryGet(typeof(ChatMessageReceivedEvent), out IAutomationEventDescriptor? chat)
            .Should()
            .BeTrue();
        chat!.PublicName.Should().Be("Twitch.ChatMessage");

        // An event with no descriptor is invisible — default-deny, not an error.
        registry
            .TryGet(typeof(NomNomzBot.Domain.Automation.Events.AutomationTokenIssuedEvent), out _)
            .Should()
            .BeFalse("token lifecycle events are internal audit, never streamed");
    }

    [Fact]
    public void The_catalog_lists_every_wire_name_sorted()
    {
        AutomationEventRegistry registry = BuildFull();

        registry
            .Catalog.Select(c => c.PublicName)
            .Should()
            .ContainInOrder(
                "Stream.Offline",
                "Stream.Online",
                "Supporter.Received",
                "Twitch.ChatMessage",
                "Twitch.RaidReceived"
            );
        registry.Catalog.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Description));
    }

    [Fact]
    public void A_duplicate_wire_name_or_double_claim_fails_fast()
    {
        Action duplicateName = () =>
            _ = new AutomationEventRegistry([
                new ChatMessageEventDescriptor(),
                new ChatMessageEventDescriptor(),
            ]);
        duplicateName.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void The_supporter_projection_is_PII_safe()
    {
        SupporterEventReceived domainEvent = new()
        {
            BroadcasterId = Guid.NewGuid(),
            SourceKey = "streamlabs",
            Kind = "tip",
            SupporterDisplayName = "GenerousViewer",
            SupporterUserId = Guid.NewGuid(),
            AmountMinor = 500,
            Currency = "EUR",
            MessageText = "great stream!",
            IsRecurring = false,
            SupporterEventId = Guid.NewGuid(),
        };

        object payload = new SupporterReceivedEventDescriptor().ProjectPayload(domainEvent);
        JsonDocument wire = JsonSerializer.SerializeToDocument(payload);
        List<string> keys = [.. wire.RootElement.EnumerateObject().Select(p => p.Name)];

        keys.Should()
            .BeEquivalentTo(
                [
                    "kind",
                    "supporterDisplayName",
                    "amountMinor",
                    "currency",
                    "tier",
                    "quantity",
                    "message",
                    "isRecurring",
                ],
                "the internal row id, supporter user id, and source key never reach the wire"
            );
        wire.RootElement.GetProperty("supporterDisplayName")
            .GetString()
            .Should()
            .Be("GenerousViewer");
    }
}
