// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Services;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tts;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves a channel with NO TtsConfig row resolves to a working effective config (a speakable voice even when
/// the catalogue has no default row), that each default lives in one place (the entity initializer), and that
/// the Azure region written through the config update reads back.
/// </summary>
public sealed class TtsConfigDefaultsTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000f01");

    private static (TtsConfigService Sut, TtsTestDbContext Db) Build()
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        TtsConfigService sut = new(
            db,
            Substitute.For<ITtsService>(),
            new RecordingEventBus(),
            Substitute.For<ISubjectKeyService>(),
            Substitute.For<Application.Identity.Services.IUserService>(),
            new PlatformTtsVoiceDefault(db),
            Substitute.For<ITtsLexiconService>(),
            new SpokenNameFormatter()
        );
        return (sut, db);
    }

    [Fact]
    public async Task A_channel_with_no_row_and_no_default_catalogue_voice_still_gets_a_speakable_voice()
    {
        (TtsConfigService sut, TtsTestDbContext db) = Build();
        (await db.TtsVoices.AnyAsync()).Should().BeFalse("the catalogue is empty in this scenario");

        Result<TtsConfigDto> result = await sut.GetConfigAsync(Channel);

        result.IsSuccess.Should().BeTrue();
        result.Value.DefaultVoiceId.Should().Be("en-US-AriaNeural");
        result.Value.DefaultProvider.Should().Be("edge");
        result.Value.FollowsPlatformDefaultVoice.Should().BeTrue();
        (await db.TtsConfigs.CountAsync()).Should().Be(0, "reads never write");
    }

    [Fact]
    public async Task The_catalogue_default_row_wins_over_the_fallback_voice()
    {
        (TtsConfigService sut, TtsTestDbContext db) = Build();
        db.TtsVoices.Add(
            new()
            {
                Id = "en-GB-SoniaNeural",
                Name = "SoniaNeural",
                DisplayName = "Sonia (GB)",
                Locale = "en-GB",
                Gender = "Female",
                Provider = "edge",
                IsDefault = true,
            }
        );
        await db.SaveChangesAsync();

        Result<TtsConfigDto> result = await sut.GetConfigAsync(Channel);

        result.Value.DefaultVoiceId.Should().Be("en-GB-SoniaNeural");
    }

    [Fact]
    public async Task The_default_config_equals_the_entity_defaults_for_every_setting()
    {
        (TtsConfigService sut, _) = Build();
        TtsConfig entity = new();

        Result<TtsConfigDto> result = await sut.GetDefaultConfigAsync();

        result.Value.IsEnabled.Should().Be(entity.IsEnabled);
        result.Value.Mode.Should().Be(entity.Mode);
        result.Value.DefaultProvider.Should().Be(entity.DefaultProvider);
        result.Value.MaxCharacters.Should().Be(entity.MaxCharacters);
        result.Value.MinPermission.Should().Be(entity.MinPermission);
        result.Value.SkipBotMessages.Should().Be(entity.SkipBotMessages);
        result.Value.ReadUsernames.Should().Be(entity.ReadUsernames);
        result.Value.ProfanityCensorEnabled.Should().Be(entity.ProfanityCensorEnabled);
        result.Value.ModApprovalRequired.Should().Be(entity.ModApprovalRequired);
        result.Value.MinBitsToTts.Should().Be(entity.MinBitsToTts);
        result
            .Value.ViewerVoiceSelfServiceEnabled.Should()
            .Be(entity.ViewerVoiceSelfServiceEnabled);
    }

    [Fact]
    public void The_read_dto_declares_no_default_values_of_its_own()
    {
        ParameterInfo[] parameters = typeof(TtsConfigDto).GetConstructors()[0].GetParameters();

        parameters
            .Where(p => p.HasDefaultValue)
            .Select(p => p.Name)
            .Should()
            .BeEmpty("the entity initializer is the one place a default is declared");
    }

    [Fact]
    public async Task The_azure_region_written_through_the_update_reads_back_and_is_persisted()
    {
        (TtsConfigService sut, TtsTestDbContext db) = Build();

        Result<TtsConfigDto> saved = await sut.UpdateConfigAsync(
            Channel,
            new() { AzureRegion = "westeurope" }
        );
        Result<TtsConfigDto> read = await sut.GetConfigAsync(Channel);

        saved.Value.AzureRegion.Should().Be("westeurope");
        read.Value.AzureRegion.Should().Be("westeurope");
        (await db.TtsConfigs.SingleAsync()).AzureRegion.Should().Be("westeurope");
    }
}
