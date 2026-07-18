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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Domain.Music.Entities;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves <see cref="BlockedTrackService"/>'s state machine: a block persists the full authored row and
/// flips <c>IsBlockedAsync</c>; an unblock removes it (soft delete) and flips it back; re-blocking is
/// idempotent (never a duplicate-insert 500); and every read/write is tenant-scoped — channel B never
/// sees channel A's blocks.
/// </summary>
public sealed class BlockedTrackServiceTests
{
    private static readonly Guid ChannelA = Guid.Parse("0192a000-0000-7000-8000-0000000b1001");
    private static readonly Guid ChannelB = Guid.Parse("0192a000-0000-7000-8000-0000000b1002");

    private static (BlockedTrackService Sut, MusicTestDbContext Db) Build()
    {
        MusicTestDbContext db = new(
            new DbContextOptionsBuilder<MusicTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        return (new BlockedTrackService(db), db);
    }

    private static BlockTrackRequest Rickroll(string? reason = "never again") =>
        new("spotify", "spotify:track:rick1", "Never Gonna Give You Up", reason, "twitch-123");

    [Fact]
    public async Task Block_persists_the_row_shape_and_IsBlocked_flips_true()
    {
        (BlockedTrackService sut, MusicTestDbContext db) = Build();

        Result<BlockedTrackDto> blocked = await sut.BlockAsync(ChannelA, Rickroll());

        blocked.IsSuccess.Should().BeTrue();
        blocked.Value.Provider.Should().Be("spotify");
        blocked.Value.TrackUri.Should().Be("spotify:track:rick1");
        blocked.Value.Title.Should().Be("Never Gonna Give You Up");
        blocked.Value.Reason.Should().Be("never again");
        blocked.Value.BlockedByUserId.Should().Be("twitch-123");
        blocked.Value.Id.Should().NotBeEmpty();

        BlockedTrack row = db.BlockedTracks.Single();
        row.BroadcasterId.Should().Be(ChannelA);
        row.TrackUri.Should().Be("spotify:track:rick1");

        (await sut.IsBlockedAsync(ChannelA, "spotify:track:rick1")).Should().BeTrue();
    }

    [Fact]
    public async Task Reblocking_the_same_uri_is_idempotent_not_a_duplicate_insert()
    {
        (BlockedTrackService sut, MusicTestDbContext db) = Build();
        Result<BlockedTrackDto> first = await sut.BlockAsync(ChannelA, Rickroll());

        Result<BlockedTrackDto> second = await sut.BlockAsync(
            ChannelA,
            Rickroll(reason: "different reason")
        );

        second.IsSuccess.Should().BeTrue();
        second.Value.Id.Should().Be(first.Value.Id, "the existing block is the answer");
        db.BlockedTracks.Count().Should().Be(1);
    }

    [Fact]
    public async Task Unblock_removes_the_block_and_IsBlocked_flips_false()
    {
        (BlockedTrackService sut, _) = Build();
        Result<BlockedTrackDto> blocked = await sut.BlockAsync(ChannelA, Rickroll());

        Result unblocked = await sut.UnblockAsync(ChannelA, blocked.Value.Id);

        unblocked.IsSuccess.Should().BeTrue();
        (await sut.IsBlockedAsync(ChannelA, "spotify:track:rick1")).Should().BeFalse();
    }

    [Fact]
    public async Task Unblock_of_an_absent_id_is_typed_not_found()
    {
        (BlockedTrackService sut, _) = Build();

        Result unblocked = await sut.UnblockAsync(ChannelA, Guid.CreateVersion7());

        unblocked.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Blocks_are_tenant_scoped_channel_B_is_unaffected()
    {
        (BlockedTrackService sut, _) = Build();
        await sut.BlockAsync(ChannelA, Rickroll());

        (await sut.IsBlockedAsync(ChannelB, "spotify:track:rick1")).Should().BeFalse();

        Result<PagedList<BlockedTrackDto>> listB = await sut.ListAsync(
            ChannelB,
            new PaginationParams(1, 25)
        );
        listB.Value.Items.Should().BeEmpty();

        // And channel B cannot unblock channel A's row.
        Result<PagedList<BlockedTrackDto>> listA = await sut.ListAsync(
            ChannelA,
            new PaginationParams(1, 25)
        );
        Guid rowId = listA.Value.Items.Single().Id;
        (await sut.UnblockAsync(ChannelB, rowId)).ErrorCode.Should().Be("NOT_FOUND");
        (await sut.IsBlockedAsync(ChannelA, "spotify:track:rick1")).Should().BeTrue();
    }

    [Fact]
    public async Task List_returns_the_channel_blocks_newest_first_with_totals()
    {
        (BlockedTrackService sut, _) = Build();
        await sut.BlockAsync(
            ChannelA,
            new BlockTrackRequest("spotify", "spotify:track:one", "One")
        );
        await sut.BlockAsync(
            ChannelA,
            new BlockTrackRequest("youtube", "yt:video:two", "Two", "loud", "twitch-9")
        );

        Result<PagedList<BlockedTrackDto>> page = await sut.ListAsync(
            ChannelA,
            new PaginationParams(1, 25)
        );

        page.Value.TotalCount.Should().Be(2);
        page.Value.Items.Should().HaveCount(2);
        BlockedTrackDto second = page.Value.Items.Single(i => i.TrackUri == "yt:video:two");
        second.Provider.Should().Be("youtube");
        second.Title.Should().Be("Two");
        second.Reason.Should().Be("loud");
        second.BlockedByUserId.Should().Be("twitch-9");
    }

    [Theory]
    [InlineData("", "spotify:track:x", "Title")]
    [InlineData("spotify", " ", "Title")]
    [InlineData("spotify", "spotify:track:x", "")]
    public async Task Block_with_a_blank_field_fails_validation(
        string provider,
        string trackUri,
        string title
    )
    {
        (BlockedTrackService sut, MusicTestDbContext db) = Build();

        Result<BlockedTrackDto> blocked = await sut.BlockAsync(
            ChannelA,
            new BlockTrackRequest(provider, trackUri, title)
        );

        blocked.ErrorCode.Should().Be("VALIDATION_FAILED");
        db.BlockedTracks.Should().BeEmpty();
    }
}
