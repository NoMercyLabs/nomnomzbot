// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Infrastructure.Tts.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves <c>tts_set_voice</c> / <c>tts_clear_voice</c> are self-service on the triggering user when no
/// <c>target_variable</c> resolves (the <c>!voice set</c> / <c>!voice clear</c> path), and act on a NAMED
/// viewer when it does (the moderator <c>!voice set @viewer</c> path) — never on both at once.
/// </summary>
public sealed class TtsVoiceAssignmentActionTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000ac301");
    private const string TriggeringUserId = "twitch-1";
    private const string TargetUserId = "twitch-99";

    private static PipelineExecutionContext Ctx(bool withTarget)
    {
        PipelineExecutionContext ctx = new()
        {
            BroadcasterId = ChannelId,
            TriggeredByUserId = TriggeringUserId,
            TriggeredByDisplayName = "Bamo",
            MessageId = "msg-1",
            RawMessage = "!cmd",
        };
        if (withTarget)
            ctx.Variables["target.id"] = TargetUserId;
        return ctx;
    }

    private static ActionDefinition SetVoiceDef(string voiceId, bool withTargetVariable) =>
        new()
        {
            Type = "tts_set_voice",
            Parameters = new()
            {
                ["voice_id"] = JsonSerializer.SerializeToElement(voiceId),
                ["target_variable"] = JsonSerializer.SerializeToElement(
                    withTargetVariable ? "target.id" : ""
                ),
            },
        };

    private static ActionDefinition ClearVoiceDef(bool withTargetVariable) =>
        new()
        {
            Type = "tts_clear_voice",
            Parameters = new()
            {
                ["target_variable"] = JsonSerializer.SerializeToElement(
                    withTargetVariable ? "target.id" : ""
                ),
            },
        };

    [Fact]
    public async Task Set_voice_with_no_target_assigns_the_triggering_users_own_voice()
    {
        ITtsConfigService tts = Substitute.For<ITtsConfigService>();
        tts.SetOwnVoiceAsync(
                ChannelId,
                TriggeringUserId,
                Arg.Any<SetUserVoiceDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new UserTtsVoiceDto(TriggeringUserId, "en-US-AnaNeural")));
        TtsSetVoiceAction action = new(tts);

        ActionResult result = await action.ExecuteAsync(
            Ctx(withTarget: false),
            SetVoiceDef("en-US-AnaNeural", withTargetVariable: false)
        );

        result.Succeeded.Should().BeTrue();
        await tts.Received(1)
            .SetOwnVoiceAsync(
                ChannelId,
                TriggeringUserId,
                Arg.Is<SetUserVoiceDto>(r => r.VoiceId == "en-US-AnaNeural"),
                Arg.Any<CancellationToken>()
            );
        await tts.DidNotReceiveWithAnyArgs().SetUserVoiceAsync(default, default!, default!);
    }

    [Fact]
    public async Task Set_voice_with_a_resolved_target_assigns_that_viewers_voice_not_the_callers()
    {
        ITtsConfigService tts = Substitute.For<ITtsConfigService>();
        tts.SetUserVoiceAsync(
                ChannelId,
                TargetUserId,
                Arg.Any<SetUserVoiceDto>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new UserTtsVoiceDto(TargetUserId, "en-US-AnaNeural")));
        TtsSetVoiceAction action = new(tts);

        ActionResult result = await action.ExecuteAsync(
            Ctx(withTarget: true),
            SetVoiceDef("en-US-AnaNeural", withTargetVariable: true)
        );

        result.Succeeded.Should().BeTrue();
        await tts.Received(1)
            .SetUserVoiceAsync(
                ChannelId,
                TargetUserId,
                Arg.Is<SetUserVoiceDto>(r => r.VoiceId == "en-US-AnaNeural"),
                Arg.Any<CancellationToken>()
            );
        await tts.DidNotReceiveWithAnyArgs().SetOwnVoiceAsync(default, default!, default!);
    }

    [Fact]
    public async Task Clear_voice_with_no_target_clears_the_triggering_users_own_voice()
    {
        ITtsConfigService tts = Substitute.For<ITtsConfigService>();
        tts.ClearOwnVoiceAsync(ChannelId, TriggeringUserId, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        TtsClearVoiceAction action = new(tts);

        ActionResult result = await action.ExecuteAsync(
            Ctx(withTarget: false),
            ClearVoiceDef(withTargetVariable: false)
        );

        result.Succeeded.Should().BeTrue();
        await tts.Received(1)
            .ClearOwnVoiceAsync(ChannelId, TriggeringUserId, Arg.Any<CancellationToken>());
        await tts.DidNotReceiveWithAnyArgs().ClearUserVoiceAsync(default, default!);
    }

    [Fact]
    public async Task Clear_voice_with_a_resolved_target_clears_that_viewers_voice_not_the_callers()
    {
        ITtsConfigService tts = Substitute.For<ITtsConfigService>();
        tts.ClearUserVoiceAsync(ChannelId, TargetUserId, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        TtsClearVoiceAction action = new(tts);

        ActionResult result = await action.ExecuteAsync(
            Ctx(withTarget: true),
            ClearVoiceDef(withTargetVariable: true)
        );

        result.Succeeded.Should().BeTrue();
        await tts.Received(1)
            .ClearUserVoiceAsync(ChannelId, TargetUserId, Arg.Any<CancellationToken>());
        await tts.DidNotReceiveWithAnyArgs().ClearOwnVoiceAsync(default, default!);
    }
}
