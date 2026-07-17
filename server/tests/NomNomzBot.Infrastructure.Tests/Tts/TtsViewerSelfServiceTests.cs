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
using Xunit;

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
            }
        );
        db.TtsConfigs.Add(
            new TtsConfig
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
            Substitute.For<ISubjectKeyService>()
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
            new SetUserVoiceDto { VoiceId = "en-GB-SoniaNeural" }
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
            new SetUserVoiceDto { VoiceId = "en-GB-SoniaNeural" }
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
            new SetUserVoiceDto { VoiceId = "en-GB-SoniaNeural" }
        );

        set.IsFailure.Should().BeTrue();
        set.ErrorCode.Should().Be("FEATURE_DISABLED");
    }

    [Fact]
    public async Task Get_own_voice_is_null_until_set_then_returns_the_pick()
    {
        (TtsConfigService config, _) = await BuildAsync();

        (await config.GetOwnVoiceAsync(Channel, ViewerId)).Value.Should().BeNull();

        await config.SetOwnVoiceAsync(
            Channel,
            ViewerId,
            new SetUserVoiceDto { VoiceId = "en-US-GuyNeural" }
        );

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
}
