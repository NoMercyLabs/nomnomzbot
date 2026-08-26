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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Services;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Tts.Entities;
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
            Substitute.For<Application.Identity.Services.IUserService>()
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
        (TtsConfigService config, _) = await BuildAsync(ttsEnabled: false);

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
        (TtsConfigService config, _) = await BuildAsync();

        (await config.GetOwnVoiceAsync(Channel, ViewerId)).Value.Should().BeNull();

        await config.SetOwnVoiceAsync(Channel, ViewerId, new() { VoiceId = "en-US-GuyNeural" });

        Result<UserTtsVoiceDto?> after = await config.GetOwnVoiceAsync(Channel, ViewerId);
        after.Value!.VoiceId.Should().Be("en-US-GuyNeural");
    }

    [Fact]
    public async Task Voice_command_searches_sets_and_reports_the_display_name()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync();
        VoiceBuiltin sut = new(config);

        Result<string> reply = await sut.ExecuteAsync(Ctx("british"));

        reply.Value.Should().Contain("Sonia (GB)");
        UserTtsVoice row = await db.UserTtsVoices.SingleAsync();
        row.VoiceId.Should().Be("en-GB-SoniaNeural");
    }

    [Fact]
    public async Task Voice_command_clear_resets_to_the_channel_default()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync();
        VoiceBuiltin sut = new(config);
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
        VoiceBuiltin sut = new(config);

        Result<string> reply = await sut.ExecuteAsync(Ctx("british"));

        reply.Value.Should().Contain("turned off");
        (await db.UserTtsVoices.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Voice_command_with_no_args_shows_the_default_hint_when_unset()
    {
        (TtsConfigService config, _) = await BuildAsync();
        VoiceBuiltin sut = new(config);

        Result<string> reply = await sut.ExecuteAsync(Ctx(""));

        reply.Value.Should().Contain("channel default");
    }

    // S053 — tts.md §6.2: !voice <full-id> and !voice <friendly-name> must resolve to the SAME real voice, and
    // an id in the wrong case must still resolve. Asserts the resolved voice identity actually persisted, not
    // merely that the command reported success.
    [Fact]
    public async Task Voice_command_full_id_and_friendly_name_resolve_to_the_same_voice()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync();
        VoiceBuiltin sut = new(config);

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
        VoiceBuiltin sut = new(config);

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
        VoiceBuiltin voice = new(config);

        Result<string> reply = await voice.ExecuteAsync(Ctx("set Ana"));

        reply.IsSuccess.Should().BeTrue(reply.ErrorMessage);
        UserTtsVoice row = await db.UserTtsVoices.SingleAsync();
        row.VoiceId.Should().Be("en-US-AnaNeural");
    }

    [Fact]
    public async Task Voice_set_by_full_id_still_wins_outright()
    {
        (TtsConfigService config, TtsTestDbContext db) = await BuildAsync();
        VoiceBuiltin voice = new(config);

        Result<string> reply = await voice.ExecuteAsync(Ctx("set en-GB-SoniaNeural"));

        reply.IsSuccess.Should().BeTrue(reply.ErrorMessage);
        (await db.UserTtsVoices.SingleAsync()).VoiceId.Should().Be("en-GB-SoniaNeural");
    }

    [Fact]
    public async Task Voice_languages_lists_every_locale_grouped_by_language()
    {
        (TtsConfigService config, _) = await BuildAsync();
        VoiceBuiltin voice = new(config);

        Result<string> reply = await voice.ExecuteAsync(Ctx("languages"));

        reply.IsSuccess.Should().BeTrue();
        reply.Value.Should().Contain("EN: en-GB, en-US");
        reply.Value.Should().Contain("AR: ar-IQ");
    }

    [Fact]
    public async Task Voice_get_by_bare_language_covers_every_locale_under_it()
    {
        (TtsConfigService config, _) = await BuildAsync();
        VoiceBuiltin voice = new(config);

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
        VoiceBuiltin voice = new(config);

        Result<string> reply = await voice.ExecuteAsync(Ctx("roulette"));

        reply.IsSuccess.Should().BeTrue(reply.ErrorMessage);
        UserTtsVoice row = await db.UserTtsVoices.SingleAsync();
        // Whatever it landed on, the announcement and the stored row are the SAME voice.
        TtsVoice stored = await db.TtsVoices.SingleAsync(v => v.Id == row.VoiceId);
        reply.Value.Should().Contain(stored.DisplayName);
    }
}
