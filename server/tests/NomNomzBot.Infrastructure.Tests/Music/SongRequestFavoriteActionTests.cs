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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music.PipelineActions;
using NSubstitute;
using RequestRecord = NomNomzBot.Domain.Platform.Entities.Record;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves the <c>song_request_favorite</c> action: queue the track a viewer has asked for more than any
/// other. Built for the modiversary celebration but deliberately generic — it reads a user id like any other
/// field, so a <c>!favorite</c> command or a raid welcome can use the same block.
///
/// <para>
/// The interesting behaviour is not "it queues something". It is that the action reports an OUTCOME the
/// pipeline can branch its copy on — queued, no history, or the music service being down are three different
/// sentences — and that a gift track is never mistaken for the viewer requesting it again.
/// </para>
/// </summary>
public sealed class SongRequestFavoriteActionTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000cc01");
    private const string Mod = "42660213";

    [Fact]
    public async Task The_track_asked_for_most_often_is_the_one_queued_and_it_is_named_for_the_announcement()
    {
        // Two favourites and one also-ran. "Most-requested" is a count over rows, so the celebration must
        // pick the two-row track — picking any row that happens to come back first would be a coin toss.
        MusicTestDbContext db = Db(
            (Mod, "spotify:track:spaghetti", "Spaghetti Code", "Developer Dreams"),
            (Mod, "spotify:track:spaghetti", "Spaghetti Code", "Developer Dreams"),
            (Mod, "spotify:track:other", "Something Else", "Someone")
        );
        (SongRequestFavoriteAction sut, IMusicService music) = Build(db);

        PipelineExecutionContext ctx = Ctx();
        ActionResult result = await sut.ExecuteAsync(ctx, Def());

        result.Succeeded.Should().BeTrue();
        await music
            .Received()
            .AddToQueueAsync(
                Channel.ToString(),
                "spotify:track:spaghetti",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>()
            );

        // The announcement is written in the dashboard, so the action's job is to hand the pipeline the
        // words. Without these the operator's copy can only say "their favourite song" and never name it.
        ctx.Variables["music.favorite.track"].Should().Be("Spaghetti Code");
        ctx.Variables["music.favorite.artist"].Should().Be("Developer Dreams");
        ctx.Variables["music.favorite.count"].Should().Be("2");
        ctx.Variables["music.favorite.outcome"].Should().Be("queued");
    }

    [Fact]
    public async Task A_refused_favorite_falls_through_to_the_next_one_instead_of_giving_up()
    {
        // The channel may have blocked the track since, or it may already be sitting in the queue. Either
        // way the viewer has a second-favourite, and a celebration that queues nothing because the top pick
        // was unavailable is a worse answer than playing their number two.
        MusicTestDbContext db = Db(
            (Mod, "spotify:track:blocked", "Blocked Banger", "Nope"),
            (Mod, "spotify:track:blocked", "Blocked Banger", "Nope"),
            (Mod, "spotify:track:second", "Second Best", "Runner Up")
        );
        (SongRequestFavoriteAction sut, IMusicService music) = Build(db);
        music
            .AddToQueueAsync(
                Channel.ToString(),
                "spotify:track:blocked",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>()
            )
            .Returns(Result.Failure("That track is blocked in this channel.", "TRACK_BLOCKED"));

        PipelineExecutionContext ctx = Ctx();
        ActionResult result = await sut.ExecuteAsync(ctx, Def());

        result.Succeeded.Should().BeTrue();
        ctx.Variables["music.favorite.track"].Should().Be("Second Best");
        ctx.Variables["music.favorite.outcome"].Should().Be("queued");
    }

    [Fact]
    public async Task A_viewer_with_no_request_history_queues_nothing_and_says_so()
    {
        // The legacy bot had a whole line for this ("modding for years, never picked a banger"). The copy
        // lives in the dashboard now, so what the action owes it is an outcome to branch on — and silence
        // from the music service, because there is nothing to play.
        MusicTestDbContext db = Db();
        (SongRequestFavoriteAction sut, IMusicService music) = Build(db);

        PipelineExecutionContext ctx = Ctx();
        ActionResult result = await sut.ExecuteAsync(ctx, Def());

        result
            .Succeeded.Should()
            .BeTrue("a viewer with no history is a normal case, not a pipeline fault");
        ctx.Variables["music.favorite.outcome"].Should().Be("none");
        await music
            .DidNotReceive()
            .AddToQueueAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>()
            );
    }

    [Fact]
    public async Task A_music_service_that_is_down_is_reported_as_its_own_outcome()
    {
        // "They have never requested a song" and "Spotify is down" must not read the same in chat. The
        // legacy bot kept two separate lines for exactly this, and only an outcome can tell them apart.
        MusicTestDbContext db = Db((Mod, "spotify:track:a", "A", "B"));
        (SongRequestFavoriteAction sut, IMusicService music) = Build(db);
        music
            .AddToQueueAsync(
                Channel.ToString(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>()
            )
            .Returns(Result.Failure("token refresh failed", "PROVIDER_ERROR"));

        PipelineExecutionContext ctx = Ctx();
        await sut.ExecuteAsync(ctx, Def());

        ctx.Variables["music.favorite.outcome"].Should().Be("unavailable");
        ctx.Variables["music.favorite.track"]
            .Should()
            .Be("A", "the copy can still name what was attempted");
    }

    [Fact]
    public async Task The_gifted_track_is_not_recorded_as_a_new_request_by_the_viewer()
    {
        // A gift is the channel playing something FOR them, not them asking for it. Attributing it would
        // let every celebration inflate the very count the next celebration reads.
        MusicTestDbContext db = Db((Mod, "spotify:track:a", "A", "B"));
        (SongRequestFavoriteAction sut, IMusicService music) = Build(db);

        await sut.ExecuteAsync(Ctx(), Def());

        await music
            .Received()
            .AddToQueueAsync(
                Channel.ToString(),
                "spotify:track:a",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>(),
                requesterUserId: null
            );
    }

    [Fact]
    public async Task Another_viewers_history_is_never_read()
    {
        // Records are keyed by user id, and the action takes that id as a field so it can serve a
        // !favorite command as well as the celebration. Reading the wrong viewer's rows would queue a
        // stranger's song under this viewer's name.
        MusicTestDbContext db = Db(
            ("someone-else", "spotify:track:theirs", "Theirs", "Them"),
            ("someone-else", "spotify:track:theirs", "Theirs", "Them"),
            (Mod, "spotify:track:mine", "Mine", "Me")
        );
        (SongRequestFavoriteAction sut, IMusicService music) = Build(db);

        PipelineExecutionContext ctx = Ctx();
        await sut.ExecuteAsync(ctx, Def());

        ctx.Variables["music.favorite.track"].Should().Be("Mine");
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private static (SongRequestFavoriteAction Sut, IMusicService Music) Build(MusicTestDbContext db)
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .AddToQueueAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>()
            )
            .Returns(Result.Success());

        SongRequestFavoriteAction sut = new(
            db,
            music,
            NullLogger<SongRequestFavoriteAction>.Instance
        );
        return (sut, music);
    }

    private static MusicTestDbContext Db(
        params (string UserId, string Uri, string Name, string Artist)[] history
    )
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        foreach ((string userId, string uri, string name, string artist) in history)
        {
            db.Records.Add(
                new RequestRecord
                {
                    BroadcasterId = Channel,
                    UserId = userId,
                    RecordType = SongRequestHistory.RecordType,
                    Data = JsonSerializer.Serialize(
                        new SongRequestHistory(uri, name, artist, null, "spotify"),
                        new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        }
                    ),
                }
            );
        }

        db.SaveChanges();
        return db;
    }

    private static ActionDefinition Def() =>
        new() { Type = "song_request_favorite", Parameters = [] };

    private static PipelineExecutionContext Ctx()
    {
        PipelineExecutionContext ctx = new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = Mod,
            TriggeredByDisplayName = "DukaSoft",
            MessageId = "m-1",
            RawMessage = string.Empty,
        };
        ctx.Variables["user.id"] = Mod;
        return ctx;
    }
}
