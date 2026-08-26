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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.Commands;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Supporters;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// S048b: <c>EventResponseService.ListAsync</c> used to top-up-seed the catalog as a side effect of
/// reading (a GET that writes), and <c>DeleteAsync</c>'s removal never stuck because the next
/// <c>ListAsync</c> silently re-inserted the deleted row. This suite proves the fix:
/// <list type="bullet">
/// <item><description><c>ListAsync</c> performs zero writes — the persisted set is byte-identical
/// before/after a call, proven on the ROWS, not the DTO shape it returns.</description></item>
/// <item><description>A deleted event response does NOT come back after a subsequent list.</description></item>
/// <item><description>The seeding moved to <see cref="EventResponseDefaultsSeeder"/> (mirrors
/// <c>DefaultCommandsSeeder</c>): a fresh channel still gets the full default set, and a channel seeded
/// before a new catalog trigger shipped still gets the missing rows — WITHOUT resurrecting a soft-deleted
/// one, because the seeder's existing-rows lookup uses <c>IgnoreQueryFilters()</c>.</description></item>
/// <item><description>A user can deliberately restore a deleted default — <c>UpsertAsync</c> revives the
/// soft-deleted row for that event type instead of leaving it orphaned.</description></item>
/// </list>
/// </summary>
public sealed class EventResponseSeedingTests
{
    private static readonly Guid Tenant = Guid.Parse("019f4b00-2222-7000-8000-000000000001");

    private static (
        EventResponseService Service,
        EventResponseDefaultsSeeder Seeder,
        SupporterTestDbContext Db
    ) Build()
    {
        SupporterTestDbContext db = SupporterTestDbContext.New();
        db.Channels.Add(
            new Channel
            {
                Id = Tenant,
                OwnerUserId = Tenant,
                Name = "seeding-test-channel",
                NameNormalized = "seeding-test-channel",
            }
        );
        db.SaveChanges();

        return (
            new EventResponseService(
                db,
                Substitute.For<IEventBus>(),
                Billing.TestQuota.Unlimited(),
                new TemplateHelperValidator()
            ),
            new EventResponseDefaultsSeeder(db),
            db
        );
    }

    private static Task ListAsync(EventResponseService service) =>
        service.ListAsync(Tenant.ToString(), new(1, 50, null, null));

    // ── ListAsync writes nothing ────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_persists_nothing_against_an_already_seeded_channel()
    {
        (
            EventResponseService service,
            EventResponseDefaultsSeeder seeder,
            SupporterTestDbContext db
        ) = Build();
        await seeder.SeedAsync(Tenant);
        List<EventResponse> before = await db
            .EventResponses.AsNoTracking()
            .OrderBy(r => r.EventType)
            .ToListAsync();

        await ListAsync(service);

        List<EventResponse> after = await db
            .EventResponses.AsNoTracking()
            .OrderBy(r => r.EventType)
            .ToListAsync();
        after
            .Should()
            .BeEquivalentTo(
                before,
                "a GET must never write — the rows are byte-identical before and after"
            );
    }

    [Fact]
    public async Task ListAsync_persists_nothing_against_an_empty_channel()
    {
        (EventResponseService service, EventResponseDefaultsSeeder _, SupporterTestDbContext db) =
            Build();

        await ListAsync(service);

        (await db.EventResponses.CountAsync())
            .Should()
            .Be(0, "ListAsync no longer seeds — that moved to the seeder");
    }

    // ── Deletion sticks ──────────────────────────────────────────────────────────

    [Fact]
    public async Task A_deleted_event_response_does_not_come_back_after_a_subsequent_list()
    {
        (
            EventResponseService service,
            EventResponseDefaultsSeeder seeder,
            SupporterTestDbContext db
        ) = Build();
        await seeder.SeedAsync(Tenant);

        Result deleteResult = await service.DeleteAsync(Tenant.ToString(), "channel.follow");
        deleteResult.IsSuccess.Should().BeTrue();

        await ListAsync(service);
        await ListAsync(service); // the exact bug the owner hit — repeated visits must not resurrect it

        List<EventResponse> rows = await db.EventResponses.AsNoTracking().ToListAsync();
        rows.Should().NotContain(r => r.EventType == "channel.follow", "the deletion must stick");

        EventResponse? softDeleted = await db
            .EventResponses.IgnoreQueryFilters()
            .SingleOrDefaultAsync(r => r.EventType == "channel.follow");
        softDeleted.Should().NotBeNull("the row survives soft-deleted, not hard-deleted");
        softDeleted!.DeletedAt.Should().NotBeNull();
    }

    // ── Deliberate restore ──────────────────────────────────────────────────────

    [Fact]
    public async Task Upserting_a_deleted_event_type_restores_it_instead_of_creating_a_duplicate()
    {
        (
            EventResponseService service,
            EventResponseDefaultsSeeder seeder,
            SupporterTestDbContext db
        ) = Build();
        await seeder.SeedAsync(Tenant);
        await service.DeleteAsync(Tenant.ToString(), "channel.follow");

        Result<EventResponseDto> restoreResult = await service.UpsertAsync(
            Tenant.ToString(),
            "channel.follow",
            new UpdateEventResponseDto { IsEnabled = true, Message = "welcome back!" }
        );

        restoreResult.IsSuccess.Should().BeTrue();
        List<EventResponse> live = await db.EventResponses.AsNoTracking().ToListAsync();
        live.Count(r => r.EventType == "channel.follow")
            .Should()
            .Be(1, "no duplicate row for the natural key");
        EventResponse restored = live.Single(r => r.EventType == "channel.follow");
        restored.DeletedAt.Should().BeNull("the operator's restore un-deletes the original row");
        restored.IsEnabled.Should().BeTrue();
        restored.Message.Should().Be("welcome back!");

        List<EventResponse> allEverStored = await db
            .EventResponses.IgnoreQueryFilters()
            .ToListAsync();
        allEverStored.Count(r => r.EventType == "channel.follow").Should().Be(1);
    }

    // ── Seeding moved to EventResponseDefaultsSeeder ────────────────────────────

    [Fact]
    public async Task A_fresh_channel_is_seeded_with_the_full_catalog_disabled()
    {
        (EventResponseService _, EventResponseDefaultsSeeder seeder, SupporterTestDbContext db) =
            Build();

        await seeder.SeedAsync(Tenant);

        List<EventResponse> rows = await db.EventResponses.ToListAsync();
        rows.Select(r => r.EventType)
            .Should()
            .BeEquivalentTo(EventResponsePresetCatalog.EventTypes);
        rows.Should().OnlyContain(r => !r.IsEnabled && r.ResponseType == "chat_message");
    }

    [Fact]
    public async Task A_newly_added_catalog_entry_still_reaches_an_existing_channel_via_the_seeder()
    {
        (EventResponseService _, EventResponseDefaultsSeeder seeder, SupporterTestDbContext db) =
            Build();
        // The pre-existing world: an old seed subset, one row customized by the operator — as if the
        // channel was seeded BEFORE "channel.raid" shipped as a catalog trigger.
        db.EventResponses.Add(
            new EventResponse
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Tenant,
                EventType = "channel.follow",
                IsEnabled = true,
                ResponseType = "chat_message",
                Message = "custom follow line",
            }
        );
        await db.SaveChangesAsync();

        await seeder.SeedAsync(Tenant);

        List<EventResponse> rows = await db.EventResponses.ToListAsync();
        rows.Select(r => r.EventType)
            .Should()
            .BeEquivalentTo(
                EventResponsePresetCatalog.EventTypes,
                "missing types are topped up for an EXISTING channel"
            );
        rows.Count(r => r.EventType == "channel.follow").Should().Be(1, "no duplicate rows");
        EventResponse follow = rows.Single(r => r.EventType == "channel.follow");
        follow.IsEnabled.Should().BeTrue("the operator's existing config is untouched");
        follow.Message.Should().Be("custom follow line");
    }

    [Fact]
    public async Task Deleted_event_type_is_never_resurrected_by_the_topup_seeder()
    {
        (
            EventResponseService service,
            EventResponseDefaultsSeeder seeder,
            SupporterTestDbContext db
        ) = Build();
        await seeder.SeedAsync(Tenant);
        await service.DeleteAsync(Tenant.ToString(), "channel.follow");

        // Re-run the top-up pass (e.g. the next app boot) — must not resurrect the deleted row.
        await seeder.SeedAsync(Tenant);

        List<EventResponse> live = await db.EventResponses.ToListAsync();
        live.Should().NotContain(r => r.EventType == "channel.follow");

        List<EventResponse> allEverStored = await db
            .EventResponses.IgnoreQueryFilters()
            .ToListAsync();
        allEverStored
            .Count(r => r.EventType == "channel.follow")
            .Should()
            .Be(1, "still exactly one soft-deleted row, not a resurrected duplicate");
    }

    [Fact]
    public async Task A_fully_seeded_channel_gets_nothing_new_on_a_second_seeder_pass()
    {
        (EventResponseService _, EventResponseDefaultsSeeder seeder, SupporterTestDbContext db) =
            Build();
        await seeder.SeedAsync(Tenant);
        int seeded = await db.EventResponses.CountAsync();

        await seeder.SeedAsync(Tenant);

        (await db.EventResponses.CountAsync()).Should().Be(seeded);
    }
}
