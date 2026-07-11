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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.MediaShare.Dtos;
using NomNomzBot.Application.MediaShare.Services;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.MediaShare.Entities;
using NomNomzBot.Domain.MediaShare.Events;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.MediaShare;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.MediaShare;

/// <summary>
/// Proves the media-share queue's behavior (media-share.md §6): a resolved clip enqueues pending (or
/// approved when approval is off) with its real metadata, the duration cap and disallowed sources are
/// rejected, the entry cost is debited on submit and refunded on reject/skip, per-user cooldown +
/// max-queue are enforced, the eligibility gate rejects an ineligible viewer, and GetNext/MarkPlayed
/// walk the FIFO play order.
/// </summary>
public sealed class MediaShareServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192d000-0000-7000-8000-00000000c001");
    private static readonly Guid Viewer = Guid.Parse("0192d000-0000-7000-8000-00000000a001");
    private static readonly Guid Mod = Guid.Parse("0192d000-0000-7000-8000-00000000a0f0");

    private sealed class RecordingBus : IEventBus
    {
        public List<IDomainEvent> Published { get; } = [];

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
            where TEvent : class, IDomainEvent
        {
            Published.Add(@event);
            return Task.CompletedTask;
        }

        public void PublishFireAndForget<TEvent>(TEvent @event)
            where TEvent : class, IDomainEvent => Published.Add(@event);
    }

    private sealed record Harness(
        MediaShareService Sut,
        MediaShareTestDbContext Db,
        IMediaSourceResolver Resolver,
        ICurrencyAccountService Accounts,
        RecordingBus Bus,
        FakeTimeProvider Clock
    );

    private static Harness Build(ResolvedMedia? resolves = null)
    {
        MediaShareTestDbContext db = MediaShareTestDbContext.New();
        db.Users.Add(
            new User
            {
                Id = Viewer,
                TwitchUserId = "111",
                Username = "alice",
                UsernameNormalized = "alice",
                DisplayName = "Alice",
            }
        );
        db.SaveChanges();

        IMediaSourceResolver resolver = Substitute.For<IMediaSourceResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    resolves
                        ?? new ResolvedMedia(
                            MediaShareSourceType.TwitchClip,
                            "CleverClipSlug",
                            "A funny moment",
                            30,
                            "https://thumb"
                        )
                )
            );

        ICurrencyAccountService accounts = Substitute.For<ICurrencyAccountService>();
        accounts
            .PostLedgerEntryAsync(
                Arg.Any<Guid>(),
                Arg.Any<PostLedgerEntryCommand>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Ledger()));

        RecordingBus bus = new();
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 7, 11, 12, 0, 0, TimeSpan.Zero));
        MediaShareService sut = new(db, resolver, accounts, bus, clock);
        return new Harness(sut, db, resolver, accounts, bus, clock);
    }

    private static CurrencyLedgerEntryDto Ledger() =>
        new(
            42,
            1,
            Guid.NewGuid(),
            Viewer,
            0,
            0,
            "SpendMedia",
            null,
            null,
            null,
            null,
            null,
            null,
            default
        );

    private static void SeedConfig(
        MediaShareTestDbContext db,
        bool enabled = true,
        bool requireApproval = true,
        int maxDuration = 180,
        long? entryCost = null,
        int maxQueue = 20,
        int cooldown = 0,
        string? eligibilityJson = null
    )
    {
        db.MediaShareConfigs.Add(
            new MediaShareConfig
            {
                BroadcasterId = Channel,
                IsEnabled = enabled,
                RequireApproval = requireApproval,
                MaxDurationSeconds = maxDuration,
                EntryCost = entryCost,
                MaxQueueLength = maxQueue,
                PerUserCooldownSeconds = cooldown,
                EligibilityJson = eligibilityJson,
            }
        );
        db.SaveChanges();
    }

    private static SubmitMediaRequest Req(string url = "https://clips.twitch.tv/CleverClipSlug") =>
        new(url);

    [Fact]
    public async Task Submit_ValidClip_ApprovalOn_EnqueuesPending_WithMetadata()
    {
        Harness h = Build();
        SeedConfig(h.Db, requireApproval: true);

        Result<MediaShareRequestDto> result = await h.Sut.SubmitAsync(Channel, Viewer, Req());

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(MediaShareStatus.Pending);
        result.Value.SourceType.Should().Be(MediaShareSourceType.TwitchClip);
        result.Value.MediaRef.Should().Be("CleverClipSlug");
        result.Value.Title.Should().Be("A funny moment");
        result.Value.DurationSeconds.Should().Be(30);
        result.Value.QueuePosition.Should().BeNull(); // not in the play order until approved
        h.Bus.Published.OfType<MediaShareSubmittedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task Submit_ApprovalOff_EnqueuesApproved_WithQueuePosition()
    {
        Harness h = Build();
        SeedConfig(h.Db, requireApproval: false);

        Result<MediaShareRequestDto> result = await h.Sut.SubmitAsync(Channel, Viewer, Req());

        result.Value.Status.Should().Be(MediaShareStatus.Approved);
        result.Value.QueuePosition.Should().Be(1);
        h.Bus.Published.OfType<MediaShareSubmittedEvent>().Should().ContainSingle();
        h.Bus.Published.OfType<MediaSharePlaybackChangedEvent>()
            .Should()
            .ContainSingle(e => e.Status == MediaShareStatus.Approved);
    }

    [Fact]
    public async Task Submit_OverCap_RejectsDurationExceeded_NoWrite()
    {
        Harness h = Build(
            new ResolvedMedia(MediaShareSourceType.TwitchClip, "long", "Long", 240, null)
        );
        SeedConfig(h.Db, maxDuration: 180);

        Result<MediaShareRequestDto> result = await h.Sut.SubmitAsync(Channel, Viewer, Req());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("DURATION_EXCEEDED");
        (await h.Db.MediaShareRequests.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Submit_DisallowedSource_PropagatesSourceNotAllowed()
    {
        Harness h = Build();
        h.Resolver.ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<ResolvedMedia>("nope", "SOURCE_NOT_ALLOWED"));
        SeedConfig(h.Db);

        Result<MediaShareRequestDto> result = await h.Sut.SubmitAsync(
            Channel,
            Viewer,
            Req("https://evil.example/video")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("SOURCE_NOT_ALLOWED");
    }

    [Fact]
    public async Task Submit_Disabled_Fails()
    {
        Harness h = Build();
        SeedConfig(h.Db, enabled: false);

        Result<MediaShareRequestDto> result = await h.Sut.SubmitAsync(Channel, Viewer, Req());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("DISABLED");
    }

    [Fact]
    public async Task EntryCost_DebitedOnSubmit_RefundedOnReject()
    {
        Harness h = Build();
        SeedConfig(h.Db, requireApproval: true, entryCost: 100);

        Result<MediaShareRequestDto> submit = await h.Sut.SubmitAsync(Channel, Viewer, Req());
        submit.IsSuccess.Should().BeTrue();

        // Debited: a SpendMedia entry of -100.
        await h
            .Accounts.Received(1)
            .PostLedgerEntryAsync(
                Channel,
                Arg.Is<PostLedgerEntryCommand>(c =>
                    c.EntryType == nameof(CurrencyEntryType.SpendMedia) && c.Amount == -100
                ),
                Arg.Any<CancellationToken>()
            );

        await h.Sut.RejectAsync(Channel, submit.Value.Id, Mod);

        // Refunded: a RefundMedia entry of +100.
        await h
            .Accounts.Received(1)
            .PostLedgerEntryAsync(
                Channel,
                Arg.Is<PostLedgerEntryCommand>(c =>
                    c.EntryType == nameof(CurrencyEntryType.RefundMedia) && c.Amount == 100
                ),
                Arg.Any<CancellationToken>()
            );
        (await h.Db.MediaShareRequests.SingleAsync()).Status.Should().Be(MediaShareStatus.Rejected);
    }

    [Fact]
    public async Task EntryCost_RefundedOnSkip()
    {
        Harness h = Build();
        SeedConfig(h.Db, requireApproval: false, entryCost: 50);
        Result<MediaShareRequestDto> submit = await h.Sut.SubmitAsync(Channel, Viewer, Req());

        await h.Sut.SkipAsync(Channel, submit.Value.Id);

        await h
            .Accounts.Received(1)
            .PostLedgerEntryAsync(
                Channel,
                Arg.Is<PostLedgerEntryCommand>(c =>
                    c.EntryType == nameof(CurrencyEntryType.RefundMedia) && c.Amount == 50
                ),
                Arg.Any<CancellationToken>()
            );
        (await h.Db.MediaShareRequests.SingleAsync()).Status.Should().Be(MediaShareStatus.Skipped);
    }

    [Fact]
    public async Task Submit_WithinCooldown_Rejected()
    {
        Harness h = Build();
        SeedConfig(h.Db, requireApproval: false, cooldown: 60);

        (await h.Sut.SubmitAsync(Channel, Viewer, Req())).IsSuccess.Should().BeTrue();
        h.Clock.Advance(TimeSpan.FromSeconds(10));
        Result<MediaShareRequestDto> second = await h.Sut.SubmitAsync(Channel, Viewer, Req());

        second.IsFailure.Should().BeTrue();
        second.ErrorCode.Should().Be("COOLDOWN");

        // After the cooldown elapses, a submit is accepted again.
        h.Clock.Advance(TimeSpan.FromSeconds(60));
        (await h.Sut.SubmitAsync(Channel, Viewer, Req())).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Submit_QueueFull_Rejected()
    {
        Harness h = Build();
        SeedConfig(h.Db, requireApproval: false, maxQueue: 2, cooldown: 0);

        (await h.Sut.SubmitAsync(Channel, Viewer, Req())).IsSuccess.Should().BeTrue();
        (await h.Sut.SubmitAsync(Channel, Viewer, Req())).IsSuccess.Should().BeTrue();
        Result<MediaShareRequestDto> third = await h.Sut.SubmitAsync(Channel, Viewer, Req());

        third.IsFailure.Should().BeTrue();
        third.ErrorCode.Should().Be("QUEUE_FULL");
    }

    [Fact]
    public async Task GetNext_ReturnsApprovedInFifoOrder_FlipsToPlaying_MarkPlayedAdvances()
    {
        Harness h = Build();
        SeedConfig(h.Db, requireApproval: false, cooldown: 0);
        Result<MediaShareRequestDto> first = await h.Sut.SubmitAsync(Channel, Viewer, Req());
        Result<MediaShareRequestDto> second = await h.Sut.SubmitAsync(Channel, Viewer, Req());

        Result<MediaShareRequestDto?> next = await h.Sut.GetNextAsync(Channel);
        next.Value!.Id.Should().Be(first.Value.Id);
        next.Value.Status.Should().Be(MediaShareStatus.Playing);

        // While one plays, GetNext returns that same playing item (doesn't skip ahead).
        (await h.Sut.GetNextAsync(Channel))
            .Value!.Id.Should()
            .Be(first.Value.Id);

        await h.Sut.MarkPlayedAsync(Channel, first.Value.Id);
        Result<MediaShareRequestDto?> afterPlayed = await h.Sut.GetNextAsync(Channel);
        afterPlayed.Value!.Id.Should().Be(second.Value.Id);
    }

    [Fact]
    public async Task GetNext_EmptyQueue_ReturnsNull()
    {
        Harness h = Build();
        SeedConfig(h.Db);

        (await h.Sut.GetNextAsync(Channel)).Value.Should().BeNull();
    }

    [Fact]
    public async Task Approve_AppendsToPlayOrder()
    {
        Harness h = Build();
        SeedConfig(h.Db, requireApproval: true);
        Result<MediaShareRequestDto> submit = await h.Sut.SubmitAsync(Channel, Viewer, Req());

        Result<MediaShareRequestDto> approved = await h.Sut.ApproveAsync(
            Channel,
            submit.Value.Id,
            Mod
        );

        approved.Value.Status.Should().Be(MediaShareStatus.Approved);
        approved.Value.QueuePosition.Should().Be(1);
    }

    [Fact]
    public async Task Reorder_MovesApprovedItemToNewPosition()
    {
        Harness h = Build();
        SeedConfig(h.Db, requireApproval: false, cooldown: 0);
        Result<MediaShareRequestDto> a = await h.Sut.SubmitAsync(Channel, Viewer, Req());
        Result<MediaShareRequestDto> b = await h.Sut.SubmitAsync(Channel, Viewer, Req());
        Result<MediaShareRequestDto> c = await h.Sut.SubmitAsync(Channel, Viewer, Req());

        // Move c (pos 3) to the front.
        (await h.Sut.ReorderAsync(Channel, c.Value.Id, 1))
            .IsSuccess.Should()
            .BeTrue();

        MediaShareRequest cRow = await h.Db.MediaShareRequests.SingleAsync(r => r.Id == c.Value.Id);
        MediaShareRequest aRow = await h.Db.MediaShareRequests.SingleAsync(r => r.Id == a.Value.Id);
        cRow.QueuePosition.Should().Be(1);
        aRow.QueuePosition.Should().Be(2);
    }

    [Fact]
    public async Task Eligibility_SubOnly_RejectsNonSub_AllowsSub()
    {
        Harness h = Build();
        SeedConfig(h.Db, requireApproval: false, eligibilityJson: "{\"subOnly\":true}");

        // Non-sub Alice is rejected.
        Result<MediaShareRequestDto> nonSub = await h.Sut.SubmitAsync(Channel, Viewer, Req());
        nonSub.IsFailure.Should().BeTrue();
        nonSub.ErrorCode.Should().Be("NOT_ELIGIBLE");

        // Grant Alice a sub standing → accepted.
        h.Db.ChannelCommunityStandings.Add(
            new ChannelCommunityStanding
            {
                BroadcasterId = Channel,
                UserId = Viewer,
                Standing = CommunityStanding.Subscriber,
                SubTier = "1000",
            }
        );
        await h.Db.SaveChangesAsync();

        Result<MediaShareRequestDto> asSub = await h.Sut.SubmitAsync(Channel, Viewer, Req());
        asSub.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetConfig_WhenNeverSet_ReturnsSafeDefaults()
    {
        Harness h = Build();

        MediaShareConfigDto dto = (await h.Sut.GetConfigAsync(Channel)).Value;

        dto.IsEnabled.Should().BeFalse();
        dto.RequireApproval.Should().BeTrue();
        dto.MaxDurationSeconds.Should().Be(180);
        dto.MaxQueueLength.Should().Be(20);
    }

    [Fact]
    public async Task UpdateConfig_PersistsAndRoundTrips()
    {
        Harness h = Build();

        await h.Sut.UpdateConfigAsync(
            Channel,
            new UpdateMediaShareConfigRequest(true, false, true, false, 90, 250, 10, 30)
        );

        MediaShareConfigDto dto = (await h.Sut.GetConfigAsync(Channel)).Value;
        dto.IsEnabled.Should().BeTrue();
        dto.RequireApproval.Should().BeFalse();
        dto.AllowYouTube.Should().BeFalse();
        dto.MaxDurationSeconds.Should().Be(90);
        dto.EntryCost.Should().Be(250);
        dto.MaxQueueLength.Should().Be(10);
        dto.PerUserCooldownSeconds.Should().Be(30);
    }
}
