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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.AutomationApi.Services;
using NomNomzBot.Domain.Supporters.Events;
using NomNomzBot.Infrastructure.AutomationApi.Events;
using NomNomzBot.Infrastructure.AutomationApi.Stream;

namespace NomNomzBot.Infrastructure.Tests.AutomationApi;

/// <summary>
/// Proves the event bridge fan-out (automation-api.md D6): a public event reaches ONLY the sessions
/// that subscribed to its wire name AND hold scope <c>events</c> AND belong to the event's tenant —
/// and what reaches them is the PII-safe projection framed as an <c>event</c> op, never the raw
/// domain event. A session whose socket throws never breaks delivery to the others.
/// </summary>
public sealed class AutomationEventBridgeHandlerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f401");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-00000000f402");

    private static AutomationSession Session(
        Guid broadcasterId,
        IReadOnlyList<string> scopes,
        string pattern,
        List<string> sink,
        bool throws = false
    )
    {
        AutomationSession session = new()
        {
            SessionId = Guid.NewGuid().ToString(),
            Principal = new AutomationPrincipal(broadcasterId, Guid.NewGuid(), "t", scopes, null),
            SendAsync = (frame, _) =>
            {
                if (throws)
                    throw new InvalidOperationException("dead socket");
                sink.Add(frame);
                return Task.CompletedTask;
            },
        };
        session.Subscribe([pattern]);
        return session;
    }

    private static SupporterEventReceived Tip(Guid broadcasterId) =>
        new()
        {
            BroadcasterId = broadcasterId,
            SourceKey = "streamlabs",
            Kind = "tip",
            SupporterDisplayName = "GenerousViewer",
            SupporterUserId = Guid.NewGuid(),
            AmountMinor = 500,
            Currency = "EUR",
            IsRecurring = false,
            SupporterEventId = Guid.NewGuid(),
        };

    [Fact]
    public async Task Delivers_the_projection_only_to_matching_scoped_same_tenant_sessions()
    {
        AutomationSessionRegistry sessions = new();
        List<string> subscribed = [];
        List<string> wrongTenant = [];
        List<string> noScope = [];
        List<string> notSubscribed = [];
        List<string> afterDeadSocket = [];
        sessions.Register(Session(Channel, ["events"], "Supporter.Received", subscribed));
        sessions.Register(Session(OtherChannel, ["events"], "Supporter.Received", wrongTenant));
        sessions.Register(Session(Channel, ["invoke"], "Supporter.Received", noScope));
        sessions.Register(Session(Channel, ["events"], "Twitch.ChatMessage", notSubscribed));
        // A dead socket in the fan-out set must not break the healthy one registered after it.
        sessions.Register(Session(Channel, ["events"], "Supporter.Received", [], throws: true));
        sessions.Register(Session(Channel, ["events"], "*", afterDeadSocket));

        AutomationEventBridgeHandler<SupporterEventReceived> handler = new(
            new AutomationEventRegistry([new SupporterReceivedEventDescriptor()]),
            sessions,
            NullLogger<AutomationEventBridgeHandler<SupporterEventReceived>>.Instance
        );
        await handler.HandleAsync(Tip(Channel));

        subscribed.Should().HaveCount(1);
        afterDeadSocket.Should().HaveCount(1, "a throwing socket never breaks the fan-out");
        wrongTenant.Should().BeEmpty("events never cross tenants");
        noScope.Should().BeEmpty("the 'events' scope gates the stream");
        notSubscribed.Should().BeEmpty();

        JsonElement frame = JsonDocument.Parse(subscribed[0]).RootElement;
        frame.GetProperty("op").GetString().Should().Be("event");
        frame.GetProperty("type").GetString().Should().Be("Supporter.Received");
        frame.GetProperty("broadcasterId").GetGuid().Should().Be(Channel);
        JsonElement data = frame.GetProperty("data");
        data.GetProperty("supporterDisplayName").GetString().Should().Be("GenerousViewer");
        data.TryGetProperty("supporterUserId", out _)
            .Should()
            .BeFalse("the projection is PII-safe — internal ids never reach the wire");
    }

    [Fact]
    public async Task An_event_without_a_descriptor_is_never_sent()
    {
        AutomationSessionRegistry sessions = new();
        List<string> sink = [];
        sessions.Register(Session(Channel, ["events"], "*", sink));

        // The registry knows NO descriptor for this event type — default-deny.
        AutomationEventBridgeHandler<SupporterEventReceived> handler = new(
            new AutomationEventRegistry([]),
            sessions,
            NullLogger<AutomationEventBridgeHandler<SupporterEventReceived>>.Instance
        );
        await handler.HandleAsync(Tip(Channel));

        sink.Should().BeEmpty();
    }
}
