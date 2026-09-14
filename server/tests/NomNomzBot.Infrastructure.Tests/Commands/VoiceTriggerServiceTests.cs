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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Assets.Services;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// Proves the voice-trigger CRUD + report behaviors: <c>StartingCount</c> seeds <c>CurrentCount</c> exactly
/// once at creation, a report against a matching enabled trigger really increments the persisted count and
/// publishes <see cref="VoiceTriggerFiredEvent"/>, cooldown really blocks a rapid re-fire and allows one after
/// the window, and an unresolved sticker asset id is rejected at write time.
/// </summary>
public sealed class VoiceTriggerServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("019f6e00-7777-7000-8000-000000000001");
    private static readonly Guid StickerAssetId = Guid.Parse(
        "019f6e00-7777-7000-8000-0000000000aa"
    );

    private static (
        VoiceTriggerService Service,
        AuthDbContext Db,
        IChannelAssetService Assets,
        IEventBus EventBus,
        FakeTimeProvider Clock
    ) Build(bool assetExists = true)
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IChannelAssetService assets = Substitute.For<IChannelAssetService>();
        assets
            .GetAsync(Tenant, StickerAssetId, Arg.Any<CancellationToken>())
            .Returns(
                assetExists
                    ? Result.Success(
                        new ChannelAssetDto(
                            StickerAssetId,
                            "boo",
                            "Boo",
                            "image",
                            "image/png",
                            1024,
                            DateTime.UtcNow,
                            "/api/v1/assets/file/" + Tenant + "/boo"
                        )
                    )
                    : Result.Failure<ChannelAssetDto>("not found", "NOT_FOUND")
            );
        IEventBus eventBus = Substitute.For<IEventBus>();
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 9, 14, 12, 0, 0, TimeSpan.Zero));
        return (new(db, assets, eventBus, clock), db, assets, eventBus, clock);
    }

    [Fact]
    public async Task Create_seeds_the_live_count_from_StartingCount()
    {
        (VoiceTriggerService service, AuthDbContext db, _, _, _) = Build();

        Result<VoiceTriggerDto> created = await service.CreateAsync(
            Tenant.ToString(),
            new() { Word = "technically", StartingCount = 42 }
        );

        created.IsSuccess.Should().BeTrue();
        created.Value.StartingCount.Should().Be(42);
        created
            .Value.CurrentCount.Should()
            .Be(42, "the live count starts equal to the seeded value");
        (await db.VoiceTriggers.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Create_rejects_a_sticker_asset_that_does_not_resolve()
    {
        (VoiceTriggerService service, _, _, _, _) = Build(assetExists: false);

        Result<VoiceTriggerDto> created = await service.CreateAsync(
            Tenant.ToString(),
            new() { Word = "fuck", StickerAssetId = StickerAssetId }
        );

        created.IsFailure.Should().BeTrue();
        created.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Report_matches_case_insensitively_and_increments_the_real_persisted_count()
    {
        (VoiceTriggerService service, AuthDbContext db, _, IEventBus eventBus, _) = Build();
        await service.CreateAsync(
            Tenant.ToString(),
            new()
            {
                Word = "technically",
                StartingCount = 10,
                StickerAssetId = StickerAssetId,
            }
        );

        Result<VoiceTriggerReportResultDto> report = await service.ReportAsync(
            Tenant,
            "well, TECHNICALLY this is correct"
        );

        report.IsSuccess.Should().BeTrue();
        report.Value.Fired.Should().BeTrue();
        report.Value.NewCount.Should().Be(11);

        // The state change is real and persisted, not just reflected in the returned DTO.
        Domain.Commands.Entities.VoiceTrigger persisted = await db.VoiceTriggers.SingleAsync();
        persisted.CurrentCount.Should().Be(11);
        persisted.LastFiredAt.Should().NotBeNull();

        await eventBus
            .Received(1)
            .PublishAsync(
                Arg.Is<VoiceTriggerFiredEvent>(e =>
                    e.BroadcasterId == Tenant
                    && e.Word == "technically"
                    && e.NewCount == 11
                    && e.StickerImageUrl != null
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Report_with_no_matching_word_does_not_fire_anything()
    {
        (VoiceTriggerService service, AuthDbContext db, _, IEventBus eventBus, _) = Build();
        await service.CreateAsync(Tenant.ToString(), new() { Word = "technically" });

        Result<VoiceTriggerReportResultDto> report = await service.ReportAsync(
            Tenant,
            "totally unrelated sentence"
        );

        report.Value.Fired.Should().BeFalse();
        (await db.VoiceTriggers.SingleAsync()).CurrentCount.Should().Be(0);
        await eventBus
            .DidNotReceiveWithAnyArgs()
            .PublishAsync(Arg.Any<VoiceTriggerFiredEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cooldown_blocks_a_rapid_refire_and_allows_one_after_the_window()
    {
        (
            VoiceTriggerService service,
            AuthDbContext db,
            _,
            IEventBus eventBus,
            FakeTimeProvider clock
        ) = Build();
        await service.CreateAsync(
            Tenant.ToString(),
            new() { Word = "technically", CooldownSeconds = 10 }
        );

        Result<VoiceTriggerReportResultDto> first = await service.ReportAsync(
            Tenant,
            "technically"
        );
        first.Value.Fired.Should().BeTrue();
        first.Value.NewCount.Should().Be(1);

        // Still inside the 10s cooldown — must NOT fire again.
        clock.Advance(TimeSpan.FromSeconds(5));
        Result<VoiceTriggerReportResultDto> second = await service.ReportAsync(
            Tenant,
            "technically"
        );
        second.Value.Fired.Should().BeFalse("still inside the cooldown window");
        (await db.VoiceTriggers.SingleAsync()).CurrentCount.Should().Be(1);

        // Past the cooldown window — must fire.
        clock.Advance(TimeSpan.FromSeconds(6));
        Result<VoiceTriggerReportResultDto> third = await service.ReportAsync(
            Tenant,
            "technically"
        );
        third.Value.Fired.Should().BeTrue();
        third.Value.NewCount.Should().Be(2);

        await eventBus
            .Received(2)
            .PublishAsync(Arg.Any<VoiceTriggerFiredEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_clears_the_sticker_via_the_empty_guid_sentinel()
    {
        (VoiceTriggerService service, _, _, _, _) = Build();
        Guid id = (
            await service.CreateAsync(
                Tenant.ToString(),
                new() { Word = "technically", StickerAssetId = StickerAssetId }
            )
        )
            .Value
            .Id;

        Result<VoiceTriggerDto> updated = await service.UpdateAsync(
            Tenant.ToString(),
            id,
            new() { StickerAssetId = Guid.Empty }
        );

        updated.IsSuccess.Should().BeTrue();
        updated.Value.StickerAssetId.Should().BeNull();
    }

    [Fact]
    public async Task Report_persists_a_transcript_segment_against_the_current_live_stream()
    {
        (VoiceTriggerService service, AuthDbContext db, _, _, _) = Build();
        db.Streams.Add(
            new()
            {
                Id = "live-stream-1",
                ChannelId = Tenant,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
                EndedAt = null,
            }
        );
        await db.SaveChangesAsync();

        await service.ReportAsync(Tenant, "just chatting about stuff");

        Domain.Commands.Entities.VoiceTranscriptSegment segment =
            await db.VoiceTranscriptSegments.SingleAsync();
        segment.StreamId.Should().Be("live-stream-1");
        segment.Text.Should().Be("just chatting about stuff");
        segment.BroadcasterId.Should().Be(Tenant);
    }

    [Fact]
    public async Task Report_with_no_live_stream_drops_the_segment_instead_of_storing_it_orphaned()
    {
        (VoiceTriggerService service, AuthDbContext db, _, _, _) = Build();
        // A stream that already ENDED must not be picked as "current" — only EndedAt == null counts.
        db.Streams.Add(
            new()
            {
                Id = "ended-stream",
                ChannelId = Tenant,
                StartedAt = DateTimeOffset.UtcNow.AddHours(-2),
                EndedAt = DateTimeOffset.UtcNow.AddHours(-1),
            }
        );
        await db.SaveChangesAsync();

        await service.ReportAsync(Tenant, "nobody is live right now");

        (await db.VoiceTranscriptSegments.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Transcript_segments_persist_in_chronological_order_for_the_right_stream()
    {
        (VoiceTriggerService service, AuthDbContext db, _, _, FakeTimeProvider clock) = Build();
        db.Streams.Add(
            new()
            {
                Id = "live-stream-2",
                ChannelId = Tenant,
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
                EndedAt = null,
            }
        );
        await db.SaveChangesAsync();

        await service.ReportAsync(Tenant, "first thing said");
        clock.Advance(TimeSpan.FromSeconds(30));
        await service.ReportAsync(Tenant, "second thing said");
        clock.Advance(TimeSpan.FromSeconds(30));
        await service.ReportAsync(Tenant, "third thing said");

        List<Domain.Commands.Entities.VoiceTranscriptSegment> ordered = await db
            .VoiceTranscriptSegments.OrderBy(s => s.SpokenAt)
            .ToListAsync();
        ordered
            .Select(s => s.Text)
            .Should()
            .Equal("first thing said", "second thing said", "third thing said");
    }

    [Fact]
    public async Task Delete_removes_the_trigger()
    {
        (VoiceTriggerService service, AuthDbContext db, _, _, _) = Build();
        Guid id = (await service.CreateAsync(Tenant.ToString(), new() { Word = "bye" })).Value.Id;

        Result deleted = await service.DeleteAsync(Tenant.ToString(), id);

        deleted.IsSuccess.Should().BeTrue();
        (await db.VoiceTriggers.IgnoreQueryFilters().CountAsync(t => t.DeletedAt == null))
            .Should()
            .Be(0);
    }
}
