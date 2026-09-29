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
using NomNomzBot.Application.Services;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tts;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves "Reset to defaults" for the channel TTS settings: every setting the form edits goes back to the
/// entity's own defaults, the reset is scoped to the one channel, and it never reaches the things the confirm
/// text promises to leave alone — the BYOK cipher envelope, the default voice, the pronunciation lexicon, and
/// per-viewer voice assignments.
/// </summary>
public sealed class TtsConfigServiceResetTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000e01");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-000000000e02");
    private static readonly Guid DekId = Guid.Parse("0192a000-0000-7000-8000-000000000e0e");

    private static (TtsConfigService Sut, TtsTestDbContext Db, RecordingEventBus Bus) Build()
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        RecordingEventBus bus = new();
        TtsConfigService sut = new(
            db,
            Substitute.For<ITtsService>(),
            bus,
            Substitute.For<ISubjectKeyService>(),
            Substitute.For<Application.Identity.Services.IUserService>(),
            new PlatformTtsVoiceDefault(db)
        );
        return (sut, db, bus);
    }

    // A row where EVERY setting differs from its default, plus BYOK material and a pinned voice.
    private static TtsConfig NonDefaultRow(Guid broadcasterId) =>
        new()
        {
            BroadcasterId = broadcasterId,
            IsEnabled = false,
            Mode = "byok",
            DefaultProvider = "azure",
            DefaultVoiceId = "en-GB-SoniaNeural",
            MaxCharacters = 120,
            MinPermission = "moderators",
            SkipBotMessages = false,
            ReadUsernames = false,
            ProfanityCensorEnabled = false,
            ModApprovalRequired = true,
            MinBitsToTts = 100,
            ViewerVoiceSelfServiceEnabled = false,
            AzureApiKeyCipher = "azure-cipher",
            AzureApiKeyNonce = "azure-nonce",
            AzureKeyVersion = 1,
            AzureRegion = "westus2",
            ElevenLabsApiKeyCipher = "el-cipher",
            ElevenLabsApiKeyNonce = "el-nonce",
            ElevenLabsKeyVersion = 1,
            SubjectKeyId = DekId,
        };

    [Fact]
    public async Task Reset_restores_every_setting_to_the_default_and_returns_the_resulting_config()
    {
        (TtsConfigService sut, TtsTestDbContext db, _) = Build();
        db.TtsConfigs.Add(NonDefaultRow(Channel));
        await db.SaveChangesAsync();

        Result<TtsConfigDto> result = await sut.ResetConfigAsync(Channel);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        TtsConfig row = await db.TtsConfigs.SingleAsync();
        row.IsEnabled.Should().BeTrue();
        row.Mode.Should().Be("client_edge");
        row.DefaultProvider.Should().Be("edge");
        row.MaxCharacters.Should().Be(500);
        row.MinPermission.Should().Be("everyone");
        row.SkipBotMessages.Should().BeTrue();
        row.ReadUsernames.Should().BeTrue();
        row.ProfanityCensorEnabled.Should().BeTrue();
        row.ModApprovalRequired.Should().BeFalse();
        row.MinBitsToTts.Should().BeNull();
        row.ViewerVoiceSelfServiceEnabled.Should().BeTrue();

        TtsConfigDto dto = result.Value;
        dto.IsEnabled.Should().Be(row.IsEnabled);
        dto.Mode.Should().Be(row.Mode);
        dto.DefaultProvider.Should().Be(row.DefaultProvider);
        dto.MaxCharacters.Should().Be(row.MaxCharacters);
        dto.MinPermission.Should().Be(row.MinPermission);
        dto.SkipBotMessages.Should().Be(row.SkipBotMessages);
        dto.ReadUsernames.Should().Be(row.ReadUsernames);
        dto.ProfanityCensorEnabled.Should().Be(row.ProfanityCensorEnabled);
        dto.ModApprovalRequired.Should().Be(row.ModApprovalRequired);
        dto.MinBitsToTts.Should().Be(row.MinBitsToTts);
        dto.ViewerVoiceSelfServiceEnabled.Should().Be(row.ViewerVoiceSelfServiceEnabled);
    }

    [Fact]
    public async Task Reset_leaves_byok_keys_the_default_voice_the_lexicon_and_viewer_voices_untouched()
    {
        (TtsConfigService sut, TtsTestDbContext db, _) = Build();
        db.TtsConfigs.Add(NonDefaultRow(Channel));
        db.TtsLexiconEntries.Add(
            new()
            {
                BroadcasterId = Channel,
                Phrase = "brb",
                Replacement = "be right back",
            }
        );
        db.UserTtsVoices.Add(
            new()
            {
                BroadcasterId = Channel,
                UserId = "1001",
                VoiceId = "en-US-AriaNeural",
            }
        );
        await db.SaveChangesAsync();

        Result<TtsConfigDto> result = await sut.ResetConfigAsync(Channel);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        TtsConfig row = await db.TtsConfigs.SingleAsync();
        row.AzureApiKeyCipher.Should().Be("azure-cipher");
        row.AzureApiKeyNonce.Should().Be("azure-nonce");
        row.AzureKeyVersion.Should().Be(1);
        row.AzureRegion.Should().Be("westus2");
        row.ElevenLabsApiKeyCipher.Should().Be("el-cipher");
        row.ElevenLabsApiKeyNonce.Should().Be("el-nonce");
        row.ElevenLabsKeyVersion.Should().Be(1);
        row.SubjectKeyId.Should().Be(DekId);
        row.DefaultVoiceId.Should().Be("en-GB-SoniaNeural", "the voice has its own reset");

        result.Value.HasAzureByokKey.Should().BeTrue();
        result.Value.HasElevenLabsByokKey.Should().BeTrue();
        result.Value.AzureRegion.Should().Be("westus2");
        result.Value.DefaultVoiceId.Should().Be("en-GB-SoniaNeural");

        TtsLexiconEntry lexicon = await db.TtsLexiconEntries.SingleAsync();
        lexicon.Phrase.Should().Be("brb");
        lexicon.Replacement.Should().Be("be right back");
        UserTtsVoice viewerVoice = await db.UserTtsVoices.SingleAsync();
        viewerVoice.UserId.Should().Be("1001");
        viewerVoice.VoiceId.Should().Be("en-US-AriaNeural");
    }

    [Fact]
    public async Task Reset_touches_only_the_requested_channels_row()
    {
        (TtsConfigService sut, TtsTestDbContext db, _) = Build();
        db.TtsConfigs.Add(NonDefaultRow(Channel));
        db.TtsConfigs.Add(NonDefaultRow(OtherChannel));
        await db.SaveChangesAsync();

        await sut.ResetConfigAsync(Channel);

        TtsConfig other = await db.TtsConfigs.SingleAsync(c => c.BroadcasterId == OtherChannel);
        other.MaxCharacters.Should().Be(120);
        other.Mode.Should().Be("byok");
        other.ModApprovalRequired.Should().BeTrue();
    }

    [Fact]
    public async Task Reset_publishes_the_dashboard_live_sync_event()
    {
        (TtsConfigService sut, TtsTestDbContext db, RecordingEventBus bus) = Build();
        db.TtsConfigs.Add(NonDefaultRow(Channel));
        await db.SaveChangesAsync();

        await sut.ResetConfigAsync(Channel);

        bus.Published.OfType<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle(e =>
                e.BroadcasterId == Channel && e.Domain == "tts-config" && e.Action == "updated"
            );
    }

    [Fact]
    public async Task Reset_without_a_row_returns_the_defaults_and_writes_nothing()
    {
        (TtsConfigService sut, TtsTestDbContext db, RecordingEventBus bus) = Build();

        Result<TtsConfigDto> result = await sut.ResetConfigAsync(Channel);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.MaxCharacters.Should().Be(500);
        result.Value.MinPermission.Should().Be("everyone");
        (await db.TtsConfigs.CountAsync())
            .Should()
            .Be(0, "a channel with no row is already at the defaults");
        bus.Published.Should().BeEmpty("nothing changed, so nothing is announced");
    }

    [Fact]
    public async Task Defaults_are_exactly_what_a_reset_writes()
    {
        (TtsConfigService sut, TtsTestDbContext db, _) = Build();
        db.TtsConfigs.Add(NonDefaultRow(Channel));
        await db.SaveChangesAsync();

        Result<TtsConfigDto> defaults = await sut.GetDefaultConfigAsync();
        Result<TtsConfigDto> reset = await sut.ResetConfigAsync(Channel);

        defaults.IsSuccess.Should().BeTrue(defaults.ErrorMessage);
        reset.IsSuccess.Should().BeTrue(reset.ErrorMessage);
        // The dialog previews `defaults`; the write must land exactly that, for every setting.
        reset
            .Value.Should()
            .BeEquivalentTo(
                defaults.Value,
                o =>
                    o.Excluding(d => d.DefaultVoiceId)
                        .Excluding(d => d.FollowsPlatformDefaultVoice)
                        .Excluding(d => d.HasAzureByokKey)
                        .Excluding(d => d.HasElevenLabsByokKey)
                        .Excluding(d => d.AzureRegion)
            );
        (await db.TtsConfigs.CountAsync()).Should().Be(1, "reading the defaults never writes");
    }
}
