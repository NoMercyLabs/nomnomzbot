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

using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.MediaShare.Dtos;
using NomNomzBot.Application.MediaShare.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.MediaShare.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.MediaShare;
using NomNomzBot.Infrastructure.MediaShare.Persistence;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.MediaShare;

/// <summary>
/// Proves a channel with NO MediaShareConfig row resolves to the entity defaults (the one place a default is
/// declared), that the database column defaults and the read DTO carry no second copy, and that every stored
/// setting is editable through the update request and read back.
/// </summary>
public sealed class MediaShareConfigDefaultsTests
{
    private static readonly Guid Channel = Guid.Parse("0192d000-0000-7000-8000-00000000d001");
    private static readonly Guid Viewer = Guid.Parse("0192d000-0000-7000-8000-00000000d0a1");

    /// <summary>A model built from the REAL entity configuration (the test context declares its own model).</summary>
    private sealed class RealConfigurationContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseSqlite("DataSource=:memory:");

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Ignore<Channel>();
            b.ApplyConfiguration(new MediaShareConfigConfiguration());
        }
    }

    private static (MediaShareService Sut, MediaShareTestDbContext Db) Build()
    {
        MediaShareTestDbContext db = MediaShareTestDbContext.New();
        MediaShareService sut = new(
            db,
            Substitute.For<IMediaSourceResolver>(),
            Substitute.For<ICurrencyAccountService>(),
            Substitute.For<IEventBus>(),
            new FakeTimeProvider()
        );
        return (sut, db);
    }

    [Fact]
    public async Task A_channel_with_no_row_reads_the_entity_defaults_for_every_setting()
    {
        (MediaShareService sut, MediaShareTestDbContext db) = Build();
        MediaShareConfig entity = new();

        Result<MediaShareConfigDto> result = await sut.GetConfigAsync(Channel);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsEnabled.Should().Be(entity.IsEnabled);
        result.Value.RequireApproval.Should().Be(entity.RequireApproval);
        result.Value.AllowTwitchClips.Should().Be(entity.AllowTwitchClips);
        result.Value.AllowYouTube.Should().Be(entity.AllowYouTube);
        result.Value.MaxDurationSeconds.Should().Be(entity.MaxDurationSeconds);
        result.Value.EntryCost.Should().Be(entity.EntryCost);
        result.Value.EligibilityJson.Should().Be(entity.EligibilityJson);
        result.Value.MaxQueueLength.Should().Be(entity.MaxQueueLength);
        result.Value.PerUserCooldownSeconds.Should().Be(entity.PerUserCooldownSeconds);
        result.Value.ConfigSchemaVersion.Should().Be(entity.ConfigSchemaVersion);
        (await db.MediaShareConfigs.CountAsync()).Should().Be(0, "reads never write");
    }

    [Fact]
    public async Task A_submit_on_a_channel_with_no_row_is_refused_by_the_default_off_switch_without_writing()
    {
        (MediaShareService sut, MediaShareTestDbContext db) = Build();

        Result<MediaShareRequestDto> result = await sut.SubmitAsync(
            Channel,
            Viewer,
            new("https://clips.twitch.tv/CleverClipSlug")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("DISABLED");
        (await db.MediaShareConfigs.CountAsync()).Should().Be(0);
        (await db.MediaShareRequests.CountAsync()).Should().Be(0);
    }

    [Fact]
    public void The_database_column_defaults_equal_the_entity_defaults()
    {
        using RealConfigurationContext db = new();
        MediaShareConfig entity = new();
        IEntityType type = db.Model.FindEntityType(typeof(MediaShareConfig))!;

        type.FindProperty(nameof(MediaShareConfig.RequireApproval))!
            .GetDefaultValue()
            .Should()
            .Be(entity.RequireApproval);
        type.FindProperty(nameof(MediaShareConfig.AllowTwitchClips))!
            .GetDefaultValue()
            .Should()
            .Be(entity.AllowTwitchClips);
        type.FindProperty(nameof(MediaShareConfig.AllowYouTube))!
            .GetDefaultValue()
            .Should()
            .Be(entity.AllowYouTube);
        type.FindProperty(nameof(MediaShareConfig.MaxDurationSeconds))!
            .GetDefaultValue()
            .Should()
            .Be(entity.MaxDurationSeconds);
        type.FindProperty(nameof(MediaShareConfig.MaxQueueLength))!
            .GetDefaultValue()
            .Should()
            .Be(entity.MaxQueueLength);
        type.FindProperty(nameof(MediaShareConfig.PerUserCooldownSeconds))!
            .GetDefaultValue()
            .Should()
            .Be(entity.PerUserCooldownSeconds);
        type.FindProperty(nameof(MediaShareConfig.ConfigSchemaVersion))!
            .GetDefaultValue()
            .Should()
            .Be(entity.ConfigSchemaVersion);
    }

    [Fact]
    public void The_dtos_declare_no_default_values_of_their_own()
    {
        foreach (
            Type dto in new[] { typeof(MediaShareConfigDto), typeof(UpdateMediaShareConfigRequest) }
        )
        {
            ParameterInfo[] parameters = dto.GetConstructors()[0].GetParameters();

            parameters
                .Where(p => p.HasDefaultValue)
                .Select(p => p.Name)
                .Should()
                .BeEmpty(
                    $"the entity initializer is the one place a default is declared ({dto.Name})"
                );
        }
    }

    [Fact]
    public async Task Every_stored_setting_written_through_the_update_reads_back_and_is_persisted()
    {
        (MediaShareService sut, MediaShareTestDbContext db) = Build();

        Result<MediaShareConfigDto> saved = await sut.UpdateConfigAsync(
            Channel,
            new(true, false, false, true, 90, 250, 10, 30, "{\"subOnly\":true}")
        );
        Result<MediaShareConfigDto> read = await sut.GetConfigAsync(Channel);

        saved.IsSuccess.Should().BeTrue();
        read.Value.Should().Be(saved.Value);
        read.Value.EligibilityJson.Should().Be("{\"subOnly\":true}");
        MediaShareConfig row = await db.MediaShareConfigs.SingleAsync();
        row.EligibilityJson.Should().Be("{\"subOnly\":true}");
        row.AllowTwitchClips.Should().BeFalse();
        row.EntryCost.Should().Be(250);
    }

    [Fact]
    public async Task An_update_without_eligibility_rules_keeps_the_stored_rules()
    {
        (MediaShareService sut, MediaShareTestDbContext db) = Build();
        await sut.UpdateConfigAsync(
            Channel,
            new(true, true, true, true, 180, null, 20, 60, "{\"subOnly\":true}")
        );

        await sut.UpdateConfigAsync(Channel, new(true, true, true, true, 120, null, 20, 60, null));

        (await db.MediaShareConfigs.SingleAsync())
            .EligibilityJson.Should()
            .Be("{\"subOnly\":true}");
    }

    [Fact]
    public async Task An_empty_eligibility_string_clears_the_rules_back_to_everyone()
    {
        (MediaShareService sut, MediaShareTestDbContext db) = Build();
        await sut.UpdateConfigAsync(
            Channel,
            new(true, true, true, true, 180, null, 20, 60, "{\"subOnly\":true}")
        );

        Result<MediaShareConfigDto> cleared = await sut.UpdateConfigAsync(
            Channel,
            new(true, true, true, true, 180, null, 20, 60, "")
        );

        cleared.Value.EligibilityJson.Should().BeNull();
        (await db.MediaShareConfigs.SingleAsync()).EligibilityJson.Should().BeNull();
    }

    [Fact]
    public async Task Malformed_eligibility_json_is_rejected_and_nothing_is_stored()
    {
        (MediaShareService sut, MediaShareTestDbContext db) = Build();

        Result<MediaShareConfigDto> result = await sut.UpdateConfigAsync(
            Channel,
            new(true, true, true, true, 180, null, 20, 60, "{not json")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.MediaShareConfigs.CountAsync()).Should().Be(0);
    }
}
