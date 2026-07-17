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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Content.Billing;
using NomNomzBot.Infrastructure.Tests.Commands;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Supporters;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Billing;

/// <summary>
/// Proves the seeded tier limits are ENFORCED, not decorative (monetization-billing.md §3.3): an at-cap
/// create is refused with <c>QUOTA_EXCEEDED</c> and persists nothing; the per-trigger variation cap guards
/// create AND update; the event-response cap counts ENABLED responses (never the lazily-seeded disabled
/// rows); and the unlimited (-1, self-host) shape gates nothing.
/// </summary>
public sealed class TierQuotaEnforcementTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000ee01");

    // ─── custom_commands + response variations ──────────────────────────────

    private static CommandService Commands(
        CommandsTestDbContext db,
        Application.Contracts.Billing.IBillingTierService tiers
    ) =>
        new(
            db,
            Substitute.For<IPipelineEngine>(),
            Substitute.For<IChannelRegistry>(),
            new RecordingEventBus(),
            tiers
        );

    [Fact]
    public async Task Command_create_at_the_cap_is_refused_and_persists_nothing()
    {
        CommandsTestDbContext db = CommandsTestDbContext.New();
        CommandService sut = Commands(db, TestTiers.WithLimit("custom_commands", 2));

        (await sut.CreateAsync(Channel.ToString(), new() { Name = "one" }))
            .IsSuccess.Should()
            .BeTrue();
        (await sut.CreateAsync(Channel.ToString(), new() { Name = "two" }))
            .IsSuccess.Should()
            .BeTrue();

        Result<CommandDto> third = await sut.CreateAsync(
            Channel.ToString(),
            new() { Name = "three" }
        );

        third.ErrorCode.Should().Be("QUOTA_EXCEEDED");
        (await db.Commands.CountAsync(c => c.BroadcasterId == Channel)).Should().Be(2);
    }

    [Fact]
    public async Task Command_variation_list_over_the_cap_is_refused_on_create_and_update()
    {
        CommandsTestDbContext db = CommandsTestDbContext.New();
        CommandService sut = Commands(
            db,
            TestTiers.WithLimit("response_variations_per_trigger", 2)
        );

        Result<CommandDto> over = await sut.CreateAsync(
            Channel.ToString(),
            new() { Name = "multi", TemplateResponses = ["a", "b", "c"] }
        );
        over.ErrorCode.Should().Be("QUOTA_EXCEEDED");

        (
            await sut.CreateAsync(
                Channel.ToString(),
                new() { Name = "multi", TemplateResponses = ["a", "b"] }
            )
        )
            .IsSuccess.Should()
            .BeTrue();

        Result<CommandDto> updated = await sut.UpdateAsync(
            Channel.ToString(),
            "multi",
            new() { TemplateResponses = ["a", "b", "c"] }
        );
        updated.ErrorCode.Should().Be("QUOTA_EXCEEDED");
        (await db.Commands.SingleAsync(c => c.NameNormalized == "multi"))
            .TemplateResponses.Should()
            .HaveCount(2, "the refused update must not have written the oversized list");
    }

    // ─── timers ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Timer_create_at_the_cap_is_refused_and_persists_nothing()
    {
        CommandsTestDbContext db = CommandsTestDbContext.New();
        TimerManagementService sut = new(
            db,
            new RecordingEventBus(),
            TestTiers.WithLimit("timers", 1)
        );

        (await sut.CreateAsync(Channel.ToString(), new() { Name = "first", Messages = ["hi"] }))
            .IsSuccess.Should()
            .BeTrue();

        Result<TimerDto> second = await sut.CreateAsync(
            Channel.ToString(),
            new() { Name = "second", Messages = ["yo"] }
        );

        second.ErrorCode.Should().Be("QUOTA_EXCEEDED");
        (await db.Timers.CountAsync(t => t.BroadcasterId == Channel)).Should().Be(1);
    }

    // ─── event_responses (enabled-count semantics) ──────────────────────────

    [Fact]
    public async Task Event_response_cap_counts_enabled_rows_and_blocks_the_next_enable()
    {
        SupporterTestDbContext db = SupporterTestDbContext.New();
        EventResponseService sut = new(
            db,
            new RecordingEventBus(),
            TestTiers.WithLimit("event_responses", 1)
        );

        // First enable fits the cap.
        (
            await sut.UpsertAsync(
                Channel.ToString(),
                "channel.follow",
                new() { IsEnabled = true, Message = "welcome {{user.name}}" }
            )
        )
            .IsSuccess.Should()
            .BeTrue();

        // A second ENABLE is over the cap...
        Result<EventResponseDto> second = await sut.UpsertAsync(
            Channel.ToString(),
            "channel.raid",
            new() { IsEnabled = true, Message = "raid!" }
        );
        second.ErrorCode.Should().Be("QUOTA_EXCEEDED");

        // ...but editing the ALREADY-enabled one is not an enable — never blocked.
        (
            await sut.UpsertAsync(
                Channel.ToString(),
                "channel.follow",
                new() { Message = "hi {{user.name}}" }
            )
        )
            .IsSuccess.Should()
            .BeTrue();

        (await db.EventResponses.CountAsync(e => e.BroadcasterId == Channel && e.IsEnabled))
            .Should()
            .Be(1);
    }

    // ─── unlimited (self-host) shape ─────────────────────────────────────────

    [Fact]
    public async Task Unlimited_tiers_gate_nothing()
    {
        CommandsTestDbContext db = CommandsTestDbContext.New();
        CommandService sut = Commands(db, TestTiers.Unlimited());

        for (int i = 0; i < 5; i++)
            (
                await sut.CreateAsync(
                    Channel.ToString(),
                    new() { Name = $"cmd{i}", TemplateResponses = ["a", "b", "c", "d"] }
                )
            )
                .IsSuccess.Should()
                .BeTrue();
    }

    // ─── seeder backfill ─────────────────────────────────────────────────────

    [Fact]
    public async Task Seeder_backfills_new_limit_keys_onto_existing_tiers_without_touching_values()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        // First seed run — the full catalogue lands, incl. sandbox_exec_ms.
        await new BillingTierSeeder(db).SeedAsync();
        await db.SaveChangesAsync();

        // Simulate an older deployment: drop one key and tune another's value.
        Domain.Billing.Entities.BillingTier baseTier = await db.BillingTiers.SingleAsync(t =>
            t.Key == "base"
        );
        Domain.Billing.Entities.TierLimit sandbox = await db.TierLimits.SingleAsync(l =>
            l.TierId == baseTier.Id && l.LimitKey == "sandbox_exec_ms"
        );
        db.TierLimits.Remove(sandbox);
        Domain.Billing.Entities.TierLimit tuned = await db.TierLimits.SingleAsync(l =>
            l.TierId == baseTier.Id && l.LimitKey == "custom_commands"
        );
        tuned.LimitValue = 12345; // operator-tuned — the seeder must never overwrite it
        await db.SaveChangesAsync();

        await new BillingTierSeeder(db).SeedAsync();
        await db.SaveChangesAsync();

        // The dropped key came back at its catalogue value; the tuned value survived.
        (
            await db.TierLimits.SingleAsync(l =>
                l.TierId == baseTier.Id && l.LimitKey == "sandbox_exec_ms"
            )
        )
            .LimitValue.Should()
            .Be(300_000);
        (
            await db.TierLimits.SingleAsync(l =>
                l.TierId == baseTier.Id && l.LimitKey == "custom_commands"
            )
        )
            .LimitValue.Should()
            .Be(12345);
    }
}
