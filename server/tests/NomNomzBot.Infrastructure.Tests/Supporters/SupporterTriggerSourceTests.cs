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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Supporters.Events;
using NomNomzBot.Infrastructure.Supporters.EventHandlers;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Supporters;

/// <summary>
/// Proves the trigger source (supporter-events.md §4): one <see cref="SupporterEventReceived"/> fires the bound
/// responses for BOTH the specific <c>supporter.&lt;kind&gt;</c> and the catch-all <c>supporter.any</c>, running
/// each configured chat message, and always logs the event to the activity feed. Assertions are on the actual
/// sends (with the resolved template) and the persisted <see cref="ChannelEvent"/>.
/// </summary>
public sealed class SupporterTriggerSourceTests
{
    private static readonly Guid Tenant = Guid.Parse("019f2900-3333-7000-8000-000000000001");

    private static (
        SupporterTriggerSource Handler,
        SupporterTestDbContext Db,
        IChatProvider Chat
    ) Build()
    {
        SupporterTestDbContext db = SupporterTestDbContext.New();

        ITemplateResolver templates = Substitute.For<ITemplateResolver>();
        templates
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo => Task.FromResult($"resolved:{callInfo.ArgAt<string>(0)}"));

        IChatProvider chat = Substitute.For<IChatProvider>();
        chat.SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        SupporterTriggerSource handler = new(
            db,
            Substitute.For<IPipelineEngine>(),
            templates,
            chat,
            NullLogger<SupporterTriggerSource>.Instance
        );
        return (handler, db, chat);
    }

    private static SupporterEventReceived TipEvent() =>
        new()
        {
            BroadcasterId = Tenant,
            SourceKey = "kofi",
            Kind = "tip",
            SupporterDisplayName = "Alice",
            AmountMinor = 500,
            Currency = "USD",
            IsRecurring = false,
            SupporterEventId = Guid.CreateVersion7(),
        };

    private static async Task BindResponseAsync(
        SupporterTestDbContext db,
        string eventType,
        string message
    )
    {
        db.EventResponses.Add(
            new EventResponse
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Tenant,
                EventType = eventType,
                ResponseType = "chat_message",
                Message = message,
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task HandleAsync_FiresBothTheKindAndAnyResponses_AndLogsActivity()
    {
        (SupporterTriggerSource handler, SupporterTestDbContext db, IChatProvider chat) = Build();
        await BindResponseAsync(db, "supporter.tip", "kind template");
        await BindResponseAsync(db, "supporter.any", "any template");

        await handler.HandleAsync(TipEvent());

        // Both bound responses ran — the specific kind and the catch-all.
        await chat.Received(1)
            .SendMessageAsync(Tenant, "resolved:kind template", Arg.Any<CancellationToken>());
        await chat.Received(1)
            .SendMessageAsync(Tenant, "resolved:any template", Arg.Any<CancellationToken>());

        // The event is on the activity feed regardless of any bound response.
        ChannelEvent logged = await db.ChannelEvents.SingleAsync();
        logged.Type.Should().Be("supporter.tip");
    }

    [Fact]
    public async Task HandleAsync_NoBoundResponse_SendsNothingButStillLogs()
    {
        (SupporterTriggerSource handler, SupporterTestDbContext db, IChatProvider chat) = Build();

        await handler.HandleAsync(TipEvent());

        await chat.DidNotReceive()
            .SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await db.ChannelEvents.CountAsync()).Should().Be(1);
    }
}
