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
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Tests.Supporters;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// Proves the event-responses page's TOP-UP seeding: a fresh channel gets one disabled row per catalog
/// event type; a channel seeded BEFORE a new trigger shipped gets exactly the missing rows added on its
/// next visit (its existing rows — including operator edits — untouched); a fully seeded channel gets
/// nothing new. The seed set is the preset catalog itself, so the page always shows every configurable
/// trigger.
/// </summary>
public sealed class EventResponseSeedingTests
{
    private static readonly Guid Tenant = Guid.Parse("019f4b00-2222-7000-8000-000000000001");

    private static (EventResponseService Service, SupporterTestDbContext Db) Build()
    {
        SupporterTestDbContext db = SupporterTestDbContext.New();
        return (
            new EventResponseService(
                db,
                Substitute.For<IEventBus>(),
                Billing.TestTiers.Unlimited()
            ),
            db
        );
    }

    private static Task ListAsync(EventResponseService service) =>
        service.ListAsync(Tenant.ToString(), new PaginationParams(1, 50, null, null));

    [Fact]
    public async Task A_fresh_channel_is_seeded_with_the_full_catalog_disabled()
    {
        (EventResponseService service, SupporterTestDbContext db) = Build();

        await ListAsync(service);

        List<EventResponse> rows = await db.EventResponses.ToListAsync();
        rows.Select(r => r.EventType)
            .Should()
            .BeEquivalentTo(EventResponsePresetCatalog.EventTypes);
        rows.Should().OnlyContain(r => !r.IsEnabled && r.ResponseType == "chat_message");
    }

    [Fact]
    public async Task A_channel_seeded_before_a_new_trigger_shipped_gets_only_the_missing_rows()
    {
        (EventResponseService service, SupporterTestDbContext db) = Build();
        // The pre-existing world: an old seed subset, one row customized by the operator.
        db.EventResponses.AddRange(
            new EventResponse
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Tenant,
                EventType = "channel.follow",
                IsEnabled = true,
                ResponseType = "chat_message",
                Message = "custom follow line",
            },
            new EventResponse
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Tenant,
                EventType = "channel.raid",
                IsEnabled = false,
                ResponseType = "chat_message",
            }
        );
        await db.SaveChangesAsync();

        await ListAsync(service);

        List<EventResponse> rows = await db.EventResponses.ToListAsync();
        rows.Select(r => r.EventType)
            .Should()
            .BeEquivalentTo(EventResponsePresetCatalog.EventTypes, "missing types are topped up");
        rows.Count(r => r.EventType == "channel.follow").Should().Be(1, "no duplicate rows");
        EventResponse follow = rows.Single(r => r.EventType == "channel.follow");
        follow.IsEnabled.Should().BeTrue("the operator's existing config is untouched");
        follow.Message.Should().Be("custom follow line");
    }

    [Fact]
    public async Task A_fully_seeded_channel_gets_nothing_new_on_revisit()
    {
        (EventResponseService service, SupporterTestDbContext db) = Build();
        await ListAsync(service);
        int seeded = await db.EventResponses.CountAsync();

        await ListAsync(service);

        (await db.EventResponses.CountAsync()).Should().Be(seeded);
    }
}
