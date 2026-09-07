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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music.Replay;
using NSubstitute;
using RequestRecord = NomNomzBot.Domain.Platform.Entities.Record;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves the song-request history rebuild reads a channel's OWN journal and nothing else. Measured against
/// the owner's real legacy data, redemptions plus <c>!sr</c> lines recover ~89% of the tally; the rest are
/// requests typed as a search phrase, whose resolved track was the provider's answer and was never journaled.
/// So the two behaviours that matter are: every recoverable request becomes exactly one row, and every
/// unrecoverable one is REPORTED rather than guessed at.
/// </summary>
public sealed class SongRequestHistoryBackfillTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000dd01");

    private static readonly SongRequestBackfillOptions Options = new(
        RewardTitles: ["Spotify Song Request"],
        CommandNames: ["!sr"]
    );

    [Fact]
    public async Task A_song_request_redemption_becomes_one_row_owned_by_the_viewer_who_redeemed_it()
    {
        (SongRequestHistoryBackfill sut, MusicTestDbContext db) = Build(
            Redemption(
                "42660213",
                "Spotify Song Request",
                "https://open.spotify.com/track/5pQm6DRBsKUvwqw3fLuifB?si=abc"
            )
        );

        Result<SongRequestBackfillSummary> run = await sut.RunAsync(Channel, Options);

        run.IsSuccess.Should().BeTrue();
        run.Value.RequestsFound.Should().Be(1);
        run.Value.RowsWritten.Should().Be(1);

        RequestRecord row = db.Records.Single();
        row.RecordType.Should().Be("SongRequest");
        row.UserId.Should().Be("42660213");
        SongRequestHistory history = Read(row);
        // The id is lifted out of whatever link shape the viewer pasted and normalised to a provider uri —
        // the same value a live request stores, so replayed and live rows count together.
        history.TrackUri.Should().Be("spotify:track:5pQm6DRBsKUvwqw3fLuifB");
    }

    [Fact]
    public async Task A_redemption_of_a_different_reward_is_not_a_song_request()
    {
        // "Steal the Lucky Feather" and "Text-to-Speech Message" are redeemed in the same stream and land in
        // the same journal. Counting them would hand every viewer a favourite song they never asked for.
        (SongRequestHistoryBackfill sut, MusicTestDbContext db) = Build(
            Redemption(
                "42660213",
                "Steal the Lucky Feather",
                "https://open.spotify.com/track/5pQm6DRBsKUvwqw3fLuifB"
            )
        );

        Result<SongRequestBackfillSummary> run = await sut.RunAsync(Channel, Options);

        run.Value.RequestsFound.Should().Be(0);
        db.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task An_sr_chat_line_counts_but_a_message_merely_mentioning_a_track_does_not()
    {
        // Roughly a third of the recoverable history came through chat rather than the reward, so !sr has to
        // count. But the channel is full of people pasting links in conversation, and the bot itself echoes
        // them back — counting any line with a track id in it over-counted and changed who owned the top track.
        (SongRequestHistoryBackfill sut, MusicTestDbContext db) = Build(
            ChatMessage("111", "!sr https://open.spotify.com/track/2ygMBIctKIAfbEBcT9065L"),
            ChatMessage(
                "222",
                "love this one https://open.spotify.com/track/71cEGt2SrA5toSCCTVpLMU"
            ),
            ChatMessage(
                "333",
                "!srsomethingelse https://open.spotify.com/track/05wIrZSwuaVWhcv5FfqeH0"
            )
        );

        Result<SongRequestBackfillSummary> run = await sut.RunAsync(Channel, Options);

        run.Value.RowsWritten.Should().Be(1);
        db.Records.Single().UserId.Should().Be("111");
    }

    [Fact]
    public async Task A_request_typed_as_a_search_phrase_is_reported_and_never_guessed_at()
    {
        // "!sr never gonna give you up" only became a track because the old bot asked Spotify, and that
        // answer was never journaled. Inventing one here would put a song in a viewer's history they never
        // picked — the count has to admit the gap instead.
        (SongRequestHistoryBackfill sut, MusicTestDbContext db) = Build(
            ChatMessage("111", "!sr never gonna give you up"),
            Redemption("222", "Spotify Song Request", "some song by some band")
        );

        Result<SongRequestBackfillSummary> run = await sut.RunAsync(Channel, Options);

        run.Value.UnresolvableFreeText.Should().Be(2);
        run.Value.RowsWritten.Should().Be(0);
        db.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task Running_it_twice_does_not_count_anybodys_favourite_twice()
    {
        // The single most damaging failure mode: a rebuild is exactly the thing an operator re-runs when
        // unsure whether it worked, and a doubled tally is invisible until it picks the wrong song.
        (SongRequestHistoryBackfill sut, MusicTestDbContext db) = Build(
            Redemption(
                "42660213",
                "Spotify Song Request",
                "https://open.spotify.com/track/5pQm6DRBsKUvwqw3fLuifB"
            )
        );

        await sut.RunAsync(Channel, Options);
        Result<SongRequestBackfillSummary> second = await sut.RunAsync(Channel, Options);

        second.Value.AlreadyPresent.Should().Be(1);
        second.Value.RowsWritten.Should().Be(0);
        db.Records.Should().ContainSingle();
    }

    [Fact]
    public async Task A_dry_run_reports_what_it_would_do_and_writes_nothing()
    {
        // The operator sees the blast radius before the history of 81 viewers is rewritten.
        (SongRequestHistoryBackfill sut, MusicTestDbContext db) = Build(
            Redemption(
                "42660213",
                "Spotify Song Request",
                "https://open.spotify.com/track/5pQm6DRBsKUvwqw3fLuifB"
            )
        );

        Result<SongRequestBackfillSummary> run = await sut.RunAsync(
            Channel,
            Options with
            {
                DryRun = true,
            }
        );

        run.Value.RequestsFound.Should().Be(1);
        run.Value.RowsWritten.Should().Be(0);
        db.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task A_live_written_row_is_never_mistaken_for_an_imported_one()
    {
        // Live rows carry no source event. If the dedupe treated them as claiming one, a backfill would skip
        // real history — or worse, re-import events the live writer had already recorded.
        (SongRequestHistoryBackfill sut, MusicTestDbContext db) = Build(
            Redemption(
                "42660213",
                "Spotify Song Request",
                "https://open.spotify.com/track/5pQm6DRBsKUvwqw3fLuifB"
            )
        );
        db.Records.Add(
            new RequestRecord
            {
                BroadcasterId = Channel,
                UserId = "42660213",
                RecordType = SongRequestHistory.RecordType,
                Data = JsonSerializer.Serialize(
                    new SongRequestHistory("spotify:track:live", "Live", "Writer", null, "spotify"),
                    Json
                ),
            }
        );
        db.SaveChanges();

        Result<SongRequestBackfillSummary> run = await sut.RunAsync(Channel, Options);

        run.Value.RowsWritten.Should().Be(1);
        db.Records.Should().HaveCount(2);
    }

    // ─── Harness ──────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static SongRequestHistory Read(RequestRecord row) =>
        JsonSerializer.Deserialize<SongRequestHistory>(row.Data, Json)!;

    private static (SongRequestHistoryBackfill Sut, MusicTestDbContext Db) Build(
        params EventRecord[] journal
    )
    {
        MusicTestDbContext db = MusicTestDbContext.New();

        IEventJournal events = Substitute.For<IEventJournal>();
        events
            .ReadStreamAsync(Channel, Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                long after = call.ArgAt<long>(1);
                return Result.Success<IReadOnlyList<EventRecord>>([
                    .. journal.Where(e => e.StreamPosition > after),
                ]);
            });

        IEventPayloadProtector payloads = Substitute.For<IEventPayloadProtector>();
        payloads
            .UnprotectAsync(Arg.Any<EventRecord>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success(call.ArgAt<EventRecord>(0).PayloadJson));

        SongRequestHistoryBackfill sut = new(
            db,
            events,
            payloads,
            NullLogger<SongRequestHistoryBackfill>.Instance
        );
        return (sut, db);
    }

    private static int _position;

    private static EventRecord Event(string eventType, string payloadJson) =>
        new(
            Id: ++_position,
            EventId: Guid.CreateVersion7(),
            BroadcasterId: Channel,
            StreamPosition: _position,
            EventType: eventType,
            EventVersion: 1,
            Source: "twitch",
            PayloadJson: payloadJson,
            PayloadIsEncrypted: false,
            SubjectKeyId: null,
            CorrelationId: null,
            CausationId: null,
            ActorUserId: null,
            ActorExternalUserId: null,
            ActorProvider: null,
            MetadataJson: "{}",
            OccurredAt: DateTime.UtcNow,
            RecordedAt: DateTime.UtcNow
        );

    private static EventRecord Redemption(string userId, string rewardTitle, string userInput) =>
        Event(
            "RewardRedeemedEvent",
            JsonSerializer.Serialize(
                new
                {
                    RewardTitle = rewardTitle,
                    UserId = userId,
                    UserInput = userInput,
                }
            )
        );

    private static EventRecord ChatMessage(string userId, string message) =>
        Event(
            "ChatMessageReceivedEvent",
            JsonSerializer.Serialize(new { UserId = userId, Message = message })
        );

    [Fact]
    public async Task A_request_the_legacy_import_already_holds_is_not_counted_twice_by_the_journal()
    {
        // THE ledger rule. The old bot's tally and this journal describe the same requests — its own table was
        // itself backfilled from these very redemptions (1,049 of 1,050 verified on the real data). Summing
        // them would roughly double every viewer's count. Timestamps cannot separate them either: the old bot
        // wrote 1,082 of its rows at one instant, so "same second" means nothing.
        (SongRequestHistoryBackfill sut, MusicTestDbContext db) = Build(
            Redemption("42660213", "Spotify Song Request", Link("5pQm6DRBsKUvwqw3fLuifB"))
        );
        SeedLegacy(db, "42660213", "spotify:track:5pQm6DRBsKUvwqw3fLuifB", count: 1);

        Result<SongRequestBackfillSummary> run = await sut.RunAsync(Channel, Options);

        run.Value.RowsWritten.Should().Be(0, "the legacy import already holds this request");
        db.Records.Should().ContainSingle();
    }

    [Fact]
    public async Task The_ledger_keeps_whichever_source_saw_more_of_the_same_request()
    {
        // The journal saw this viewer ask for the track three times; the legacy tally only caught one. Keeping
        // the legacy count alone would understate a favourite, and adding them would overstate it by four.
        // The truth is the larger observation: three.
        (SongRequestHistoryBackfill sut, MusicTestDbContext db) = Build(
            Redemption("42660213", "Spotify Song Request", Link("5pQm6DRBsKUvwqw3fLuifB")),
            Redemption("42660213", "Spotify Song Request", Link("5pQm6DRBsKUvwqw3fLuifB")),
            Redemption("42660213", "Spotify Song Request", Link("5pQm6DRBsKUvwqw3fLuifB"))
        );
        SeedLegacy(db, "42660213", "spotify:track:5pQm6DRBsKUvwqw3fLuifB", count: 1);

        Result<SongRequestBackfillSummary> run = await sut.RunAsync(Channel, Options);

        run.Value.RowsWritten.Should().Be(2);
        db.Records.Should().HaveCount(3);
    }

    [Fact]
    public async Task A_legacy_request_for_a_different_track_never_masks_a_journal_one()
    {
        // Reconciliation is per (viewer, track). If it collapsed to the viewer, one legacy row would swallow
        // every other song they ever asked for.
        (SongRequestHistoryBackfill sut, MusicTestDbContext db) = Build(
            Redemption("42660213", "Spotify Song Request", Link("2ygMBIctKIAfbEBcT9065L"))
        );
        SeedLegacy(db, "42660213", "spotify:track:5pQm6DRBsKUvwqw3fLuifB", count: 3);

        Result<SongRequestBackfillSummary> run = await sut.RunAsync(Channel, Options);

        run.Value.RowsWritten.Should().Be(1);
        db.Records.Should().HaveCount(4);
    }

    private static string Link(string trackId) => $"https://open.spotify.com/track/{trackId}";

    private static void SeedLegacy(MusicTestDbContext db, string userId, string uri, int count)
    {
        for (int i = 0; i < count; i++)
        {
            db.Records.Add(
                new RequestRecord
                {
                    BroadcasterId = Channel,
                    UserId = userId,
                    RecordType = SongRequestHistory.RecordType,
                    Data = JsonSerializer.Serialize(
                        new SongRequestHistory(
                            uri,
                            string.Empty,
                            string.Empty,
                            null,
                            "spotify",
                            SourceRef: $"legacy:{Guid.NewGuid()}"
                        ),
                        Json
                    ),
                }
            );
        }

        db.SaveChanges();
    }
}
