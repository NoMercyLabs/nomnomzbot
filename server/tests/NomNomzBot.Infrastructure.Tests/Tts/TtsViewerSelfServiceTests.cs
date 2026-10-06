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
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NomNomzBot.Infrastructure.Tts;
using NomNomzBot.Infrastructure.Tts.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves viewer self-service voice (tts.md §3.6/§6.1, decision 6): a viewer sets their OWN voice keyed by
/// their platform id (what the dispatch resolver reads), gated by the channel toggle. The <c>!voice</c> command
/// searches the catalogue, sets the best match, persists it, and reports it; <c>!voice clear</c> resets; and a
/// channel that locks self-service off gets a friendly refusal with nothing written.
/// </summary>
public sealed class TtsViewerSelfServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000e01");
    private const string ViewerId = "viewer-1";

    // These tests never name an unseen viewer, so the Twitch lookup and user minting stay unused.
    private static ITwitchUsersApi NoTwitch() => Substitute.For<ITwitchUsersApi>();

    private static IUserService NoUsers() => Substitute.For<IUserService>();

    private static async Task<(TtsConfigService Config, TtsTestDbContext Db)> BuildAsync(
        bool selfServiceEnabled = true,
        bool ttsEnabled = true
    )
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        db.TtsVoices.AddRange(
            new TtsVoice
            {
                Id = "en-GB-SoniaNeural",
                Name = "SoniaNeural",
                DisplayName = "Sonia (GB)",
                Locale = "en-GB",
                Gender = "Female",
                Provider = "edge",
                Accent = "British",
            },
            new TtsVoice
            {
                Id = "en-US-GuyNeural",
                Name = "GuyNeural",
                DisplayName = "Guy (US)",
                Locale = "en-US",
                Gender = "Male",
                Provider = "edge",
                Accent = "American",
            },
            // The "Ana" collision, in catalogue order: both of these sort BEFORE en-US-AnaNeural and both
            // contain the substring "ana", which is exactly how `!voice set Ana` used to land on Rana.
            new TtsVoice
            {
                Id = "ar-IQ-RanaNeural",
                Name = "RanaNeural",
                DisplayName = "Rana (IQ)",
                Locale = "ar-IQ",
                Gender = "Female",
                Provider = "edge",
            },
            new TtsVoice
            {
                Id = "ca-ES-JoanaNeural",
                Name = "JoanaNeural",
                DisplayName = "Joana (ES)",
                Locale = "ca-ES",
                Gender = "Female",
                Provider = "edge",
            },
            new TtsVoice
            {
                Id = "en-US-AnaNeural",
                Name = "AnaNeural",
                DisplayName = "Ana (US)",
                Locale = "en-US",
                Gender = "Female",
                Provider = "edge",
                Accent = "American",
            }
        );
        db.TtsConfigs.Add(
            new()
            {
                BroadcasterId = Channel,
                IsEnabled = ttsEnabled,
                ViewerVoiceSelfServiceEnabled = selfServiceEnabled,
            }
        );
        await db.SaveChangesAsync();

        TtsConfigService config = new(
            db,
            Substitute.For<ITtsService>(),
            Substitute.For<IEventBus>(),
            Substitute.For<ISubjectKeyService>(),
            Substitute.For<Application.Identity.Services.IUserService>(),
            new PlatformTtsVoiceDefault(db)
        );
        return (config, db);
    }

    private static BuiltinCommandContext Ctx(string args) =>
        new()
        {
            BroadcasterId = Channel,
            TriggeringUserId = ViewerId,
            TriggeringUserDisplayName = "Viewer",
            TriggeringUserLogin = "viewer",
            Args = args,
        };

    [Fact]
    public async Task Set_own_voice_persists_the_pick_keyed_by_the_viewer_platform_id()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync();

        Result<UserTtsVoiceDto> set = await config.SetOwnVoiceAsync(
            Channel,
            ViewerId,
            new() { VoiceId = "en-GB-SoniaNeural" }
        );

        set.IsSuccess.Should().BeTrue();
        UserTtsVoice row = await db.UserTtsVoices.SingleAsync();
        row.UserId.Should().Be(ViewerId);
        row.VoiceId.Should().Be("en-GB-SoniaNeural");
    }

    [Fact]
    public async Task Set_own_voice_is_refused_when_the_channel_locks_self_service()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync(
            selfServiceEnabled: false
        );

        Result<UserTtsVoiceDto> set = await config.SetOwnVoiceAsync(
            Channel,
            ViewerId,
            new() { VoiceId = "en-GB-SoniaNeural" }
        );

        set.IsFailure.Should().BeTrue();
        set.ErrorCode.Should().Be("FEATURE_DISABLED");
        (await db.UserTtsVoices.AnyAsync()).Should().BeFalse("nothing is written on a refusal");
    }

    [Fact]
    public async Task Set_own_voice_is_refused_when_tts_is_disabled()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync(ttsEnabled: false);

        Result<UserTtsVoiceDto> set = await config.SetOwnVoiceAsync(
            Channel,
            ViewerId,
            new() { VoiceId = "en-GB-SoniaNeural" }
        );

        set.IsFailure.Should().BeTrue();
        set.ErrorCode.Should().Be("FEATURE_DISABLED");
    }

    [Fact]
    public async Task Get_own_voice_is_null_until_set_then_returns_the_pick()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync();

        (await config.GetOwnVoiceAsync(Channel, ViewerId)).Value.Should().BeNull();

        await config.SetOwnVoiceAsync(Channel, ViewerId, new() { VoiceId = "en-US-GuyNeural" });

        Result<UserTtsVoiceDto?> after = await config.GetOwnVoiceAsync(Channel, ViewerId);
        after.Value!.VoiceId.Should().Be("en-US-GuyNeural");
    }

    [Fact]
    public async Task Voice_command_searches_sets_and_reports_the_display_name()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync();
        VoiceBuiltin sut = new(config, db, TestBuiltinComposer.Create(), NoTwitch(), NoUsers());

        Result<string> reply = await sut.ExecuteAsync(Ctx("british"));

        reply.Value.Should().Contain("Sonia (GB)");
        UserTtsVoice row = await db.UserTtsVoices.SingleAsync();
        row.VoiceId.Should().Be("en-GB-SoniaNeural");
    }

    [Fact]
    public async Task Voice_command_clear_resets_to_the_channel_default()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync();
        VoiceBuiltin sut = new(config, db, TestBuiltinComposer.Create(), NoTwitch(), NoUsers());
        await sut.ExecuteAsync(Ctx("guy"));

        Result<string> reply = await sut.ExecuteAsync(Ctx("clear"));

        reply.Value.Should().Contain("channel default");
        (await db.UserTtsVoices.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Voice_command_refuses_and_writes_nothing_when_self_service_is_locked()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync(
            selfServiceEnabled: false
        );
        VoiceBuiltin sut = new(config, db, TestBuiltinComposer.Create(), NoTwitch(), NoUsers());

        Result<string> reply = await sut.ExecuteAsync(Ctx("british"));

        reply.Value.Should().Contain("turned off");
        (await db.UserTtsVoices.AnyAsync()).Should().BeFalse();
    }

    // S053 — tts.md §6.2: !voice <full-id> and !voice <friendly-name> must resolve to the SAME real voice, and
    // an id in the wrong case must still resolve. Asserts the resolved voice identity actually persisted, not
    // merely that the command reported success.
    [Fact]
    public async Task Voice_command_full_id_and_friendly_name_resolve_to_the_same_voice()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync();
        VoiceBuiltin sut = new(config, db, TestBuiltinComposer.Create(), NoTwitch(), NoUsers());

        Result<string> byId = await sut.ExecuteAsync(Ctx("en-GB-SoniaNeural"));
        UserTtsVoice afterId = await db.UserTtsVoices.SingleAsync();
        afterId.VoiceId.Should().Be("en-GB-SoniaNeural");
        byId.IsSuccess.Should().BeTrue();

        Result<string> byName = await sut.ExecuteAsync(Ctx("sonia"));
        UserTtsVoice afterName = await db.UserTtsVoices.SingleAsync();

        afterName
            .VoiceId.Should()
            .Be(afterId.VoiceId, "the full id and the friendly name name the same voice");
    }

    [Fact]
    public async Task Voice_command_full_id_resolves_regardless_of_case()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync();
        VoiceBuiltin sut = new(config, db, TestBuiltinComposer.Create(), NoTwitch(), NoUsers());

        Result<string> reply = await sut.ExecuteAsync(Ctx("EN-gb-SONIANEURAL"));

        reply.IsSuccess.Should().BeTrue();
        UserTtsVoice row = await db.UserTtsVoices.SingleAsync();
        row.VoiceId.Should()
            .Be(
                "en-GB-SoniaNeural",
                "the wrong-case id still resolves to the real catalogue voice"
            );
    }

    // ── !voice surface (old-bot parity) ───────────────────────────────────────────────────────────
    // A viewer types a bare speaker name. Ranking by catalogue relevance alone handed `!voice set Ana`
    // to ar-IQ-RanaNeural, because it contains "ana" and sorts first — the wrong voice, silently.

    [Fact]
    public async Task Voice_set_by_bare_speaker_name_picks_that_speaker_not_a_substring_neighbour()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync();
        VoiceBuiltin voice = new(config, db, TestBuiltinComposer.Create(), NoTwitch(), NoUsers());

        Result<string> reply = await voice.ExecuteAsync(Ctx("set Ana"));

        reply.IsSuccess.Should().BeTrue(reply.ErrorMessage);
        UserTtsVoice row = await db.UserTtsVoices.SingleAsync();
        row.VoiceId.Should().Be("en-US-AnaNeural");
    }

    [Fact]
    public async Task Voice_set_by_full_id_still_wins_outright()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync();
        VoiceBuiltin voice = new(config, db, TestBuiltinComposer.Create(), NoTwitch(), NoUsers());

        Result<string> reply = await voice.ExecuteAsync(Ctx("set en-GB-SoniaNeural"));

        reply.IsSuccess.Should().BeTrue(reply.ErrorMessage);
        (await db.UserTtsVoices.SingleAsync()).VoiceId.Should().Be("en-GB-SoniaNeural");
    }

    [Fact]
    public async Task Voice_languages_lists_every_locale_grouped_by_language()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync();
        VoiceBuiltin voice = new(config, db, TestBuiltinComposer.Create(), NoTwitch(), NoUsers());

        Result<string> reply = await voice.ExecuteAsync(Ctx("languages"));

        reply.IsSuccess.Should().BeTrue();
        reply.Value.Should().Contain("EN: en-GB, en-US");
        reply.Value.Should().Contain("AR: ar-IQ");
    }

    [Fact]
    public async Task Voice_get_by_bare_language_covers_every_locale_under_it()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync();
        VoiceBuiltin voice = new(config, db, TestBuiltinComposer.Create(), NoTwitch(), NoUsers());

        Result<string> reply = await voice.ExecuteAsync(Ctx("get en"));

        reply.IsSuccess.Should().BeTrue();
        // en-GB and en-US voices, named the way a viewer would say them; no Arabic/Catalan bleed-through.
        reply.Value.Should().Contain("Sonia");
        reply.Value.Should().Contain("Ana");
        reply.Value.Should().NotContain("Rana");
        reply.Value.Should().NotContain("Joana");
    }

    [Fact]
    public async Task Voice_roulette_keeps_the_pick_it_announces()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync();
        VoiceBuiltin voice = new(config, db, TestBuiltinComposer.Create(), NoTwitch(), NoUsers());

        Result<string> reply = await voice.ExecuteAsync(Ctx("roulette"));

        reply.IsSuccess.Should().BeTrue(reply.ErrorMessage);
        UserTtsVoice row = await db.UserTtsVoices.SingleAsync();
        // Whatever it landed on, the announcement and the stored row are the SAME voice.
        TtsVoice stored = await db.TtsVoices.SingleAsync(v => v.Id == row.VoiceId);
        reply.Value.Should().Contain(stored.DisplayName);
    }

    [Fact]
    public async Task Voice_set_speaks_the_informative_line_of_its_slot_with_the_voice_filled_in()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync();
        VoiceBuiltin sut = new(config, db, TestBuiltinComposer.Create(), NoTwitch(), NoUsers());

        Result<string> reply = await sut.ExecuteAsync(Ctx("guy"));

        reply.Value.Should().Be("✅ Voice set to Guy (US)!");
    }

    [Fact]
    public async Task Voice_locked_refusal_comes_from_the_disabled_slot()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync(
            selfServiceEnabled: false
        );
        VoiceBuiltin sut = new(config, db, TestBuiltinComposer.Create(), NoTwitch(), NoUsers());

        Result<string> reply = await sut.ExecuteAsync(Ctx("guy"));

        reply.Value.Should().Be("Picking your own voice is turned off on this channel.");
    }

    [Fact]
    public async Task Voice_channel_override_of_one_slot_replaces_exactly_that_reply()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync();
        FakeChannelBuiltinReplies own = new FakeChannelBuiltinReplies().Set(
            Channel,
            BuiltinResponseSlots.Voice.Key,
            BuiltinResponseSlots.Voice.Cleared,
            "Back to the house voice."
        );
        VoiceBuiltin sut = new(config, db, TestBuiltinComposer.Create(own), NoTwitch(), NoUsers());

        Result<string> picked = await sut.ExecuteAsync(Ctx("guy"));
        Result<string> cleared = await sut.ExecuteAsync(Ctx("clear"));

        picked.Value.Should().Be("✅ Voice set to Guy (US)!");
        cleared.Value.Should().Be("Back to the house voice.");
        (await db.UserTtsVoices.AnyAsync()).Should().BeFalse("the clear still ran");
    }

    // -- !voice legacy texts: the informative line says what the old bot said ------------------------

    private static readonly string[] LegacyRouletteLines =
    [
        "The wheel has spoken! Your next TTS message will be in... {0}. Good luck.",
        "Voice roulette says: {0}. No takebacks.",
        "Spinning the wheel... {0}! May the odds be ever in your favor.",
        "The RNG gods have chosen: {0}. We are not responsible for what happens next.",
        "And the random voice is... {0}! Chat, place your bets on how this sounds.",
        "Voice roulette has landed on {0}. This should be interesting.",
    ];

    private const string LegacyUsage =
        "Voice commands: !voice languages | !voice get <language> | !voice set <name> | !voice current | !voice roulette";

    private static async Task<string> SayAsync(string args, bool emptyCatalogue = false)
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync();
        if (emptyCatalogue)
        {
            db.TtsVoices.RemoveRange(db.TtsVoices);
            await db.SaveChangesAsync();
        }
        VoiceBuiltin sut = new(config, db, TestBuiltinComposer.Create(), NoTwitch(), NoUsers());
        Result<string> reply = await sut.ExecuteAsync(Ctx(args));
        reply.IsSuccess.Should().BeTrue(reply.ErrorMessage);
        return reply.Value;
    }

    [Fact]
    public async Task Voice_without_arguments_answers_the_legacy_usage_line() =>
        (await SayAsync("")).Should().Be(LegacyUsage);

    [Fact]
    public async Task Voice_unknown_bare_word_answers_the_legacy_unknown_command_line() =>
        (await SayAsync("zzzz"))
            .Should()
            .Be(
                "Unknown voice command. Use: !voice languages | !voice get <language> | !voice set <name> | !voice current | !voice roulette"
            );

    [Fact]
    public async Task Voice_languages_answers_the_legacy_line() =>
        (await SayAsync("languages"))
            .Should()
            .Be("Available languages: AR: ar-IQ | CA: ca-ES | EN: en-GB, en-US");

    [Fact]
    public async Task Voice_languages_with_an_empty_catalogue_answers_the_legacy_line() =>
        (await SayAsync("languages", emptyCatalogue: true)).Should().Be("No TTS voices available.");

    [Fact]
    public async Task Voice_get_without_a_language_answers_the_legacy_usage() =>
        (await SayAsync("get"))
            .Should()
            .Be("Usage: !voice get <language> (e.g. !voice get en or !voice get en-US)");

    [Fact]
    public async Task Voice_get_unknown_language_answers_the_legacy_line() =>
        (await SayAsync("get zz")).Should().Be("No voices found for 'zz'. Try !voice languages");

    [Fact]
    public async Task Voice_get_language_lists_the_voices_under_an_uppercase_language() =>
        (await SayAsync("get en")).Should().Be("EN voices: Sonia, Ana, Guy");

    [Fact]
    public async Task Voice_set_without_a_name_answers_the_legacy_usage() =>
        (await SayAsync("set"))
            .Should()
            .Be("Usage: !voice set <name> (e.g. !voice set Ana, !voice set en-US-AnaNeural)");

    [Fact]
    public async Task Voice_set_unknown_name_answers_the_legacy_not_found_line() =>
        (await SayAsync("set zzzz"))
            .Should()
            .Be("Voice 'zzzz' not found. Use !voice get <language> to see available voices.");

    [Fact]
    public async Task Voice_current_after_a_set_answers_the_legacy_line()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync();
        VoiceBuiltin sut = new(config, db, TestBuiltinComposer.Create(), NoTwitch(), NoUsers());
        await sut.ExecuteAsync(Ctx("set guy"));

        Result<string> reply = await sut.ExecuteAsync(Ctx("current"));

        reply.Value.Should().Be("Current voice: en-US-GuyNeural");
    }

    [Fact]
    public async Task Voice_current_without_own_voice_answers_the_legacy_default_line() =>
        (await SayAsync("current"))
            .Should()
            .Be("No voice set. Use !voice get <language> to find voices.");

    [Fact]
    public async Task Voice_set_with_several_equal_matches_lists_them_and_stores_nothing()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync();
        db.TtsVoices.AddRange(
            new TtsVoice
            {
                Id = "en-US-NatashaNeural",
                Name = "NatashaNeural",
                DisplayName = "Natasha (US)",
                Locale = "en-US",
                Gender = "Female",
                Provider = "edge",
            },
            new TtsVoice
            {
                Id = "en-AU-NatalieNeural",
                Name = "NatalieNeural",
                DisplayName = "Natalie (AU)",
                Locale = "en-AU",
                Gender = "Female",
                Provider = "edge",
            }
        );
        await db.SaveChangesAsync();
        VoiceBuiltin sut = new(config, db, TestBuiltinComposer.Create(), NoTwitch(), NoUsers());

        Result<string> reply = await sut.ExecuteAsync(Ctx("set nat"));

        reply.Value.Should().Be("Multiple matches: en-AU-NatalieNeural, en-US-NatashaNeural");
        (await db.UserTtsVoices.AnyAsync())
            .Should()
            .BeFalse("an ambiguous name must not pick a voice");
    }

    [Fact]
    public async Task Voice_current_without_own_voice_names_the_channel_default_voice()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync();
        TtsVoice guy = await db.TtsVoices.SingleAsync(v => v.Id == "en-US-GuyNeural");
        guy.IsDefault = true;
        await db.SaveChangesAsync();
        VoiceBuiltin sut = new(config, db, TestBuiltinComposer.Create(), NoTwitch(), NoUsers());

        Result<string> reply = await sut.ExecuteAsync(Ctx("current"));

        reply.Value.Should().Be("Using default: Guy (US). Set custom voice with !voice set <name>");
    }

    [Fact]
    public async Task Voice_roulette_with_an_empty_catalogue_answers_the_legacy_line() =>
        (await SayAsync("roulette", emptyCatalogue: true))
            .Should()
            .Be("No voices available for roulette!");

    [Fact]
    public async Task Voice_roulette_speaks_one_of_the_six_legacy_lines_with_the_stored_voice()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync();
        VoiceBuiltin sut = new(config, db, TestBuiltinComposer.Create(), NoTwitch(), NoUsers());

        Result<string> reply = await sut.ExecuteAsync(Ctx("roulette"));

        UserTtsVoice row = await db.UserTtsVoices.SingleAsync();
        TtsVoice stored = await db.TtsVoices.SingleAsync(v => v.Id == row.VoiceId);
        string shown = $"{stored.DisplayName} ({stored.Locale})";
        reply
            .Value.Should()
            .BeOneOf(LegacyRouletteLines.Select(line => string.Format(line, shown)));
    }
}
