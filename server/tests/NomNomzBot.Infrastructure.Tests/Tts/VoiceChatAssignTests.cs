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
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NomNomzBot.Infrastructure.Tts;
using NomNomzBot.Infrastructure.Tts.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves <c>!voice set @viewer &lt;voice&gt;</c> and <c>!voice @viewer set &lt;voice&gt;</c>: a moderator or the
/// broadcaster assigns THAT viewer's voice through the same service the dashboard uses; anyone else keeps to
/// their own voice and is refused when naming another viewer; an unknown viewer gets a clear reply.
/// </summary>
public sealed class VoiceChatAssignTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000e02");
    private const string CallerId = "caller-1";
    private const string TargetId = "target-9";

    private static readonly int ModeratorLevel = PermissionLevel.Moderator.ToLevelValue();
    private static readonly int BroadcasterLevel = PermissionLevel.Broadcaster.ToLevelValue();

    private static async Task<(VoiceBuiltin Sut, TtsTestDbContext Db)> BuildAsync()
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        db.TtsVoices.AddRange(
            new TtsVoice
            {
                Id = "en-GB-ThomasNeural",
                Name = "ThomasNeural",
                DisplayName = "Thomas (GB)",
                Locale = "en-GB",
                Gender = "Male",
                Provider = "edge",
            },
            new TtsVoice
            {
                Id = "en-US-GuyNeural",
                Name = "GuyNeural",
                DisplayName = "Guy (US)",
                Locale = "en-US",
                Gender = "Male",
                Provider = "edge",
            }
        );
        db.UserIdentities.Add(
            new()
            {
                UserId = Guid.CreateVersion7(),
                Provider = "twitch",
                ProviderUserId = TargetId,
                ProviderUsername = "mahybe",
                ProviderDisplayName = "Mahybe",
            }
        );
        db.TtsConfigs.Add(
            new()
            {
                BroadcasterId = Channel,
                IsEnabled = true,
                ViewerVoiceSelfServiceEnabled = true,
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
        return (new VoiceBuiltin(config, db, TestBuiltinComposer.Create()), db);
    }

    private static BuiltinCommandContext Ctx(string args, int roleLevel) =>
        new()
        {
            BroadcasterId = Channel,
            TriggeringUserId = CallerId,
            TriggeringUserDisplayName = "Caller",
            TriggeringUserLogin = "caller",
            RoleLevel = roleLevel,
            Args = args,
        };

    [Theory]
    [InlineData("set @mahybe thomas")]
    [InlineData("@mahybe set thomas")]
    public async Task Moderator_sets_the_named_viewers_voice_in_both_word_orders(string args)
    {
        (VoiceBuiltin sut, TtsTestDbContext db) = await BuildAsync();

        Result<string> reply = await sut.ExecuteAsync(Ctx(args, ModeratorLevel));

        reply.IsSuccess.Should().BeTrue(reply.ErrorMessage);
        UserTtsVoice row = await db.UserTtsVoices.SingleAsync();
        row.UserId.Should().Be(TargetId, "the voice belongs to the named viewer, not the caller");
        row.BroadcasterId.Should().Be(Channel);
        row.VoiceId.Should().Be("en-GB-ThomasNeural");
        reply.Value.Should().Be("Voice for Mahybe set to Thomas (GB)!");
    }

    [Fact]
    public async Task Broadcaster_sets_the_named_viewers_voice()
    {
        (VoiceBuiltin sut, TtsTestDbContext db) = await BuildAsync();

        Result<string> reply = await sut.ExecuteAsync(Ctx("set @MAHYBE guy", BroadcasterLevel));

        reply.IsSuccess.Should().BeTrue(reply.ErrorMessage);
        (await db.UserTtsVoices.SingleAsync()).UserId.Should().Be(TargetId);
        (await db.UserTtsVoices.SingleAsync()).VoiceId.Should().Be("en-US-GuyNeural");
    }

    [Theory]
    [InlineData("set @mahybe thomas")]
    [InlineData("@mahybe set thomas")]
    public async Task Viewer_without_moderator_rank_is_refused_and_nothing_is_written(string args)
    {
        (VoiceBuiltin sut, TtsTestDbContext db) = await BuildAsync();

        Result<string> reply = await sut.ExecuteAsync(Ctx(args, 0));

        reply.IsSuccess.Should().BeTrue(reply.ErrorMessage);
        reply
            .Value.Should()
            .Be("You can only set your own voice. Naming another viewer is for moderators.");
        (await db.UserTtsVoices.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Unknown_viewer_gets_a_clear_reply_and_nothing_is_written()
    {
        (VoiceBuiltin sut, TtsTestDbContext db) = await BuildAsync();

        Result<string> reply = await sut.ExecuteAsync(Ctx("set @nobody thomas", ModeratorLevel));

        reply.Value.Should().Be("I don't know a viewer called 'nobody' here.");
        (await db.UserTtsVoices.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Unknown_voice_for_a_named_viewer_gets_the_not_found_reply_and_nothing_is_written()
    {
        (VoiceBuiltin sut, TtsTestDbContext db) = await BuildAsync();

        Result<string> reply = await sut.ExecuteAsync(Ctx("set @mahybe zzzz", ModeratorLevel));

        reply.Value.Should().Contain("zzzz");
        (await db.UserTtsVoices.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Self_form_still_sets_the_callers_own_voice()
    {
        (VoiceBuiltin sut, TtsTestDbContext db) = await BuildAsync();

        Result<string> reply = await sut.ExecuteAsync(Ctx("set thomas", 0));

        reply.Value.Should().Be("✅ Voice set to Thomas (GB)!");
        UserTtsVoice row = await db.UserTtsVoices.SingleAsync();
        row.UserId.Should().Be(CallerId);
        row.VoiceId.Should().Be("en-GB-ThomasNeural");
    }

    [Fact]
    public async Task Naming_yourself_is_the_self_form_for_any_rank()
    {
        (VoiceBuiltin sut, TtsTestDbContext db) = await BuildAsync();

        Result<string> reply = await sut.ExecuteAsync(Ctx("set @caller thomas", 0));

        reply.Value.Should().Be("✅ Voice set to Thomas (GB)!");
        (await db.UserTtsVoices.SingleAsync()).UserId.Should().Be(CallerId);
    }
}
