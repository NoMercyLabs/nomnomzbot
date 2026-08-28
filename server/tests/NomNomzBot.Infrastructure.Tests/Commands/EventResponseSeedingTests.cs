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
/// reading (a GET that writes). S-EVENTRESPONSE-NO-CREATE then settled the model further: a row is a
/// fixed, seeded catalogue entry, never user-created or user-deletable — the old <c>DeleteAsync</c>
/// (a hard-coded <c>Remove()</c>) made "Delete" a silent, permanent loss once <c>ListAsync</c> stopped
/// top-up-seeding, contradicting the dashboard's own "reset to default" framing. It is now
/// <c>ResetToDefaultAsync</c>, which never removes the row — it resets its fields in place. This suite
/// proves:
/// <list type="bullet">
/// <item><description><c>ListAsync</c> performs zero writes — the persisted set is byte-identical
/// before/after a call, proven on the ROWS, not the DTO shape it returns.</description></item>
/// <item><description><c>ResetToDefaultAsync</c> puts the row back to its seeded default shape (disabled,
/// chat_message, no message/pipeline/metadata) and the row STAYS PRESENT and enumerable — no
/// disappearance, no soft-delete.</description></item>
/// <item><description>The seeding moved to <see cref="EventResponseDefaultsSeeder"/> (mirrors
/// <c>DefaultCommandsSeeder</c>): a fresh channel still gets the full default set, and a channel seeded
/// before a new catalog trigger shipped still gets the missing rows.</description></item>
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

    // ── Reset puts the row back to default WITHOUT removing it ───────────────────

    [Fact]
    public async Task ResetToDefault_clears_the_customized_fields_but_the_row_stays_enumerable()
    {
        (
            EventResponseService service,
            EventResponseDefaultsSeeder seeder,
            SupporterTestDbContext db
        ) = Build();
        await seeder.SeedAsync(Tenant);
        Result<EventResponseDto> customized = await service.UpsertAsync(
            Tenant.ToString(),
            "channel.follow",
            new UpdateEventResponseDto
            {
                IsEnabled = true,
                ResponseType = "pipeline",
                Message = "thanks {{user.name}}!",
                Metadata = new Dictionary<string, string> { ["widgetId"] = "w-1" },
            }
        );
        customized.IsSuccess.Should().BeTrue();

        Result resetResult = await service.ResetToDefaultAsync(Tenant.ToString(), "channel.follow");
        resetResult.IsSuccess.Should().BeTrue();

        // Two consecutive lists — the exact bug the owner hit with the old Delete: repeated visits must
        // never make the row vanish.
        await ListAsync(service);
        await ListAsync(service);

        List<EventResponse> rows = await db.EventResponses.AsNoTracking().ToListAsync();
        rows.Should().Contain(r => r.EventType == "channel.follow", "reset never removes the row");
        EventResponse reset = rows.Single(r => r.EventType == "channel.follow");
        reset.DeletedAt.Should().BeNull("reset is not a delete — the row is never soft-deleted");
        reset.IsEnabled.Should().BeFalse();
        reset.ResponseType.Should().Be("chat_message");
        reset.Message.Should().BeNull();
        reset.PipelineId.Should().BeNull();
        reset.MetadataJson.Should().BeEmpty();
    }

    [Fact]
    public async Task ResetToDefault_on_an_unconfigured_seeded_row_is_a_no_op_success()
    {
        (
            EventResponseService service,
            EventResponseDefaultsSeeder seeder,
            SupporterTestDbContext db
        ) = Build();
        await seeder.SeedAsync(Tenant);

        Result resetResult = await service.ResetToDefaultAsync(Tenant.ToString(), "channel.follow");

        resetResult.IsSuccess.Should().BeTrue();
        (await db.EventResponses.CountAsync())
            .Should()
            .Be(EventResponsePresetCatalog.EventTypes.Count);
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
    public async Task A_reset_row_is_not_touched_or_duplicated_by_a_later_topup_seeder_pass()
    {
        (
            EventResponseService service,
            EventResponseDefaultsSeeder seeder,
            SupporterTestDbContext db
        ) = Build();
        await seeder.SeedAsync(Tenant);
        await service.UpsertAsync(
            Tenant.ToString(),
            "channel.follow",
            new UpdateEventResponseDto { IsEnabled = true, Message = "custom" }
        );
        await service.ResetToDefaultAsync(Tenant.ToString(), "channel.follow");

        // Re-run the top-up pass (e.g. the next app boot) — the natural key already exists, so this must
        // not touch it or add a duplicate.
        await seeder.SeedAsync(Tenant);

        List<EventResponse> live = await db.EventResponses.ToListAsync();
        live.Count(r => r.EventType == "channel.follow").Should().Be(1, "no duplicate row");
        EventResponse follow = live.Single(r => r.EventType == "channel.follow");
        follow.IsEnabled.Should().BeFalse();
        follow.Message.Should().BeNull();
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
