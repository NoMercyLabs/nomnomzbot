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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Platform.Services;
using NomNomzBot.Infrastructure.CustomCode;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// Proves the capability broker (custom-code.md §3.2, catalogue §6.2): it grants exactly the declared capabilities
/// that exist in the catalogue with their feature-flag enabled; an unknown capability or a gated-off feature fails
/// the whole grant FORBIDDEN (fail-closed); no catalogue entry is ever a `critical` tier.
/// </summary>
public sealed class ScriptCapabilityBrokerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000c001");

    private static ScriptCapabilityBroker Build(bool featureEnabled = true)
    {
        // The broker gates on the per-channel Custom Code feature toggle (IFeatureService), NOT a platform
        // rollout FeatureFlag — the switch an owner actually flips must be the one that admits scripts.
        IFeatureService features = Substitute.For<IFeatureService>();
        features
            .IsFeatureEnabledAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(featureEnabled);
        return new ScriptCapabilityBroker(features);
    }

    [Fact]
    public async Task Grants_the_declared_known_capabilities()
    {
        ScriptCapabilityBroker sut = Build(featureEnabled: true);

        Result<ScriptCapabilityGrant> result = await sut.BuildGrantAsync(
            Channel,
            ["chat.send", "vars.read"]
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Granted.Select(g => g.Key).Should().BeEquivalentTo("chat.send", "vars.read");
    }

    [Fact]
    public async Task An_unknown_capability_is_forbidden()
    {
        ScriptCapabilityBroker sut = Build(featureEnabled: true);

        Result<ScriptCapabilityGrant> result = await sut.BuildGrantAsync(Channel, ["bot.evil"]);

        result.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task A_gated_off_feature_forbids_the_grant()
    {
        ScriptCapabilityBroker sut = Build(featureEnabled: false);

        Result<ScriptCapabilityGrant> result = await sut.BuildGrantAsync(Channel, ["chat.send"]);

        result.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task No_declared_capabilities_yields_an_empty_grant()
    {
        ScriptCapabilityBroker sut = Build();

        Result<ScriptCapabilityGrant> result = await sut.BuildGrantAsync(Channel, []);

        result.IsSuccess.Should().BeTrue();
        result.Value.Granted.Should().BeEmpty();
    }

    [Fact]
    public void The_catalogue_exposes_no_critical_capability()
    {
        ScriptCapabilityBroker sut = Build();

        sut.Catalog.Should().NotBeEmpty();
        sut.Catalog.Should().OnlyContain(c => c.FloorTier != "critical");
    }

    [Fact]
    public void The_catalogue_lists_the_storage_tts_widget_and_reward_capabilities()
    {
        ScriptCapabilityBroker sut = Build();

        sut.Catalog.Select(c => c.Key)
            .Should()
            .Contain([
                "storage.get",
                "storage.set",
                "storage.delete",
                "storage.list",
                "tts.speak",
                "widget.emit",
                "reward.get",
                "reward.update",
            ]);
        // The mutating ones are marked side-effecting; the reads are not.
        sut.Catalog.Single(c => c.Key == "storage.set").SideEffecting.Should().BeTrue();
        sut.Catalog.Single(c => c.Key == "storage.get").SideEffecting.Should().BeFalse();
        sut.Catalog.Single(c => c.Key == "reward.update").SideEffecting.Should().BeTrue();
        // reward.update mutates the reward on Twitch itself → tos tier, like the other Twitch-facing writes.
        sut.Catalog.Single(c => c.Key == "reward.update").FloorTier.Should().Be("tos");
    }

    [Fact]
    public async Task The_new_capabilities_are_grantable_when_the_feature_is_enabled()
    {
        ScriptCapabilityBroker sut = Build(featureEnabled: true);

        Result<ScriptCapabilityGrant> result = await sut.BuildGrantAsync(
            Channel,
            ["storage.set", "tts.speak", "widget.emit", "reward.update"]
        );

        result.IsSuccess.Should().BeTrue();
        result
            .Value.Granted.Select(g => g.Key)
            .Should()
            .BeEquivalentTo("storage.set", "tts.speak", "widget.emit", "reward.update");
    }

    [Fact]
    public async Task The_stats_and_voice_capabilities_are_catalogued_and_grantable()
    {
        ScriptCapabilityBroker sut = Build(featureEnabled: true);

        Result<ScriptCapabilityGrant> result = await sut.BuildGrantAsync(
            Channel,
            ["stats.viewer", "tts.voice.get", "tts.voice.set"]
        );

        result.IsSuccess.Should().BeTrue();
        result
            .Value.Granted.Select(g => g.Key)
            .Should()
            .BeEquivalentTo("stats.viewer", "tts.voice.get", "tts.voice.set");
        // Reads are read-only; the voice assignment is the side-effecting low-tier write (no Twitch surface).
        sut.Catalog.Single(c => c.Key == "stats.viewer").SideEffecting.Should().BeFalse();
        sut.Catalog.Single(c => c.Key == "tts.voice.get").SideEffecting.Should().BeFalse();
        sut.Catalog.Single(c => c.Key == "tts.voice.set").SideEffecting.Should().BeTrue();
        sut.Catalog.Single(c => c.Key == "tts.voice.set").FloorTier.Should().Be("low");
    }

    [Fact]
    public async Task An_undeclared_stats_lookalike_key_is_denied_at_grant_time()
    {
        ScriptCapabilityBroker sut = Build(featureEnabled: true);

        // Not in the catalogue (only stats.viewer is) — the whole grant fails closed.
        Result<ScriptCapabilityGrant> result = await sut.BuildGrantAsync(
            Channel,
            ["stats.viewer", "stats.channel"]
        );

        result.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task An_undeclared_lookalike_key_is_still_denied_at_grant_time()
    {
        ScriptCapabilityBroker sut = Build(featureEnabled: true);

        // Not in the catalogue (only widget.emit is) — the whole grant fails closed.
        Result<ScriptCapabilityGrant> result = await sut.BuildGrantAsync(
            Channel,
            ["widget.emit", "widget.delete"]
        );

        result.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task The_schedule_pipeline_capability_is_catalogued_and_grantable()
    {
        ScriptCapabilityBroker sut = Build(featureEnabled: true);

        Result<ScriptCapabilityGrant> result = await sut.BuildGrantAsync(
            Channel,
            ["schedule.pipeline"]
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Granted.Select(g => g.Key).Should().Contain("schedule.pipeline");
        // Side-effecting (it persists a task) but no external/Twitch surface → low tier.
        sut.Catalog.Single(c => c.Key == "schedule.pipeline").SideEffecting.Should().BeTrue();
        sut.Catalog.Single(c => c.Key == "schedule.pipeline").FloorTier.Should().Be("low");
    }

    [Fact]
    public async Task An_undeclared_schedule_lookalike_key_is_denied_at_grant_time()
    {
        ScriptCapabilityBroker sut = Build(featureEnabled: true);

        // Not in the catalogue (only schedule.pipeline is) — the whole grant fails closed.
        Result<ScriptCapabilityGrant> result = await sut.BuildGrantAsync(
            Channel,
            ["schedule.pipeline", "schedule.command"]
        );

        result.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task The_schedule_pipeline_grant_is_forbidden_when_the_feature_is_gated_off()
    {
        ScriptCapabilityBroker sut = Build(featureEnabled: false);

        Result<ScriptCapabilityGrant> result = await sut.BuildGrantAsync(
            Channel,
            ["schedule.pipeline"]
        );

        result.ErrorCode.Should().Be("FORBIDDEN");
    }
}
