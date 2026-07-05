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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Platform;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Platform.Dtos;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Infrastructure.Platform;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform;

/// <summary>
/// Proves <see cref="FeatureService"/> composes the TWO independent axes correctly: the channel's own opt-in state
/// (the <c>ChannelFeature</c> row, else the key's catalogue default) AND the platform entitlement gate consulted
/// through <see cref="IFeatureFlagService"/>. Specifically: <see cref="FeatureService.ToggleFeatureAsync"/> publishes
/// the E5 dashboard live-sync event after every successful toggle, an invalid channel id never publishes, the four
/// chat-decoration keys are registered with their own defaults, a channel with NO row toggles away from its default
/// in a single call, and — the entitlement slice — a not-entitled feature reports <c>Entitled=false</c> even while
/// its opt-in row is ON ("visible is not entitled"), the gate wins over opt-in, enabling a non-entitled feature is
/// refused <c>NOT_ENTITLED</c> with no row written, and disabling is always allowed.
/// </summary>
public sealed class FeatureServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000001001");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static (FeatureService Sut, RecordingEventBus Bus, IFeatureFlagService Flags) Build() =>
        BuildWith(FeatureServiceTestDbContext.New());

    private static (FeatureService Sut, RecordingEventBus Bus, IFeatureFlagService Flags) BuildWith(
        FeatureServiceTestDbContext db
    )
    {
        RecordingEventBus bus = new();
        IFeatureFlagService flags = Substitute.For<IFeatureFlagService>();
        // Default: NO catalogue key is gated, so the entitlement axis is always satisfied. Individual tests override
        // a specific key with a more-specific stub (NSubstitute matches the most-recently-configured call).
        flags
            .EvaluateAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new FeatureFlagEvaluation(false, false, null, null));
        return (new FeatureService(db, new FakeTimeProvider(Now), bus, flags), bus, flags);
    }

    [Fact]
    public async Task Toggle_publishes_ChannelConfigChangedEvent_for_the_features_domain()
    {
        (FeatureService sut, RecordingEventBus bus, _) = Build();

        Result<FeatureStatusDto> result = await sut.ToggleFeatureAsync(
            Channel.ToString(),
            "custom_code"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.IsEnabled.Should().BeTrue();
        bus.Published.OfType<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle(e =>
                e.BroadcasterId == Channel
                && e.Domain == "features"
                && e.EntityId == "custom_code"
                && e.Action == "toggled"
            );
    }

    [Fact]
    public async Task Toggling_twice_flips_the_state_and_publishes_each_time()
    {
        (FeatureService sut, RecordingEventBus bus, _) = Build();

        await sut.ToggleFeatureAsync(Channel.ToString(), "custom_code");
        Result<FeatureStatusDto> second = await sut.ToggleFeatureAsync(
            Channel.ToString(),
            "custom_code"
        );

        second.Value.IsEnabled.Should().BeFalse();
        bus.Published.OfType<ChannelConfigChangedEvent>().Should().HaveCount(2);
    }

    [Fact]
    public async Task An_invalid_channel_id_publishes_nothing()
    {
        (FeatureService sut, RecordingEventBus bus, _) = Build();

        Result<FeatureStatusDto> result = await sut.ToggleFeatureAsync("not-a-guid", "custom_code");

        result.IsSuccess.Should().BeFalse();
        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFeatures_reports_the_four_decoration_keys_at_their_own_default_when_no_row_exists()
    {
        (FeatureService sut, _, _) = Build();

        Result<List<FeatureStatusDto>> result = await sut.GetFeaturesAsync(Channel.ToString());

        result.IsSuccess.Should().BeTrue();
        Dictionary<string, bool> byKey = result.Value.ToDictionary(
            f => f.FeatureKey,
            f => f.IsEnabled
        );

        // Third-party emote providers default ON (the near-universal want); link preview defaults OFF (it makes
        // an outbound fetch); custom_code stays OFF (unchanged pre-existing behavior) — the bug this closes is
        // GetFeaturesAsync not returning these four keys AT ALL, so every channel's toggle was silently ignored.
        byKey.Should().Contain("use_7tv", true);
        byKey.Should().Contain("use_bttv", true);
        byKey.Should().Contain("use_ffz", true);
        byKey.Should().Contain("use_link_preview", false);
        byKey.Should().Contain("custom_code", false);
    }

    [Fact]
    public async Task GetFeatures_reports_an_explicit_row_state_over_the_key_default()
    {
        FeatureServiceTestDbContext db = FeatureServiceTestDbContext.New();
        db.ChannelFeatures.Add(
            new ChannelFeature
            {
                BroadcasterId = Channel,
                FeatureKey = "use_7tv",
                IsEnabled = false,
            }
        );
        await db.SaveChangesAsync();
        (FeatureService sut, _, _) = BuildWith(db);

        Result<List<FeatureStatusDto>> result = await sut.GetFeaturesAsync(Channel.ToString());

        result
            .Value.Single(f => f.FeatureKey == "use_7tv")
            .IsEnabled.Should()
            .BeFalse("an explicit disabled row overrides the default-ON catalogue state");
    }

    [Fact]
    public async Task Toggling_a_default_on_key_with_no_row_yet_disables_it_in_a_single_call()
    {
        FeatureServiceTestDbContext db = FeatureServiceTestDbContext.New();
        (FeatureService sut, _, _) = BuildWith(db);

        Result<FeatureStatusDto> result = await sut.ToggleFeatureAsync(
            Channel.ToString(),
            "use_7tv"
        );

        // ONE call from a channel that never touched this row must land it disabled — not enabled (which would
        // mean the channel needs a SECOND click to actually turn off a feature that was already on by default).
        result.IsSuccess.Should().BeTrue();
        result.Value.IsEnabled.Should().BeFalse();

        ChannelFeature? row = await db.ChannelFeatures.FirstOrDefaultAsync(f =>
            f.BroadcasterId == Channel && f.FeatureKey == "use_7tv"
        );
        row.Should().NotBeNull();
        row!.IsEnabled.Should().BeFalse();
        row.EnabledAt.Should().BeNull();
    }

    [Fact]
    public async Task Toggling_a_default_off_key_with_no_row_yet_enables_it_in_a_single_call()
    {
        FeatureServiceTestDbContext db = FeatureServiceTestDbContext.New();
        (FeatureService sut, _, _) = BuildWith(db);

        Result<FeatureStatusDto> result = await sut.ToggleFeatureAsync(
            Channel.ToString(),
            "use_link_preview"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.IsEnabled.Should().BeTrue();

        ChannelFeature? row = await db.ChannelFeatures.FirstOrDefaultAsync(f =>
            f.BroadcasterId == Channel && f.FeatureKey == "use_link_preview"
        );
        row.Should().NotBeNull();
        row!.IsEnabled.Should().BeTrue();
        row.EnabledAt.Should().Be(Now.UtcDateTime);
    }

    // ── Entitlement axis (ROADMAP: FeaturesController must consult IFeatureFlagService gates) ──

    [Fact]
    public async Task GetFeatures_reports_entitled_when_no_flag_governs_a_catalogue_key()
    {
        (FeatureService sut, _, _) = Build();

        Result<List<FeatureStatusDto>> result = await sut.GetFeaturesAsync(Channel.ToString());

        result
            .Value.Should()
            .OnlyContain(f => f.Entitled && f.EntitlementReason == null && f.RequiredTier == null);
    }

    [Fact]
    public async Task GetFeatures_reports_not_entitled_even_when_the_opt_in_row_is_ON()
    {
        // The audited bug: a feature reported "enabled" to the client purely because the opt-in row was on, even
        // when the channel's tier does not entitle it. The row IS on here — but the tier gate is not satisfied.
        FeatureServiceTestDbContext db = FeatureServiceTestDbContext.New();
        db.ChannelFeatures.Add(
            new ChannelFeature
            {
                BroadcasterId = Channel,
                FeatureKey = "custom_code",
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();
        (FeatureService sut, _, IFeatureFlagService flags) = BuildWith(db);
        flags
            .EvaluateAsync("custom_code", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(
                new FeatureFlagEvaluation(true, false, FeatureEntitlementReason.RequiresTier, "pro")
            );

        Result<List<FeatureStatusDto>> result = await sut.GetFeaturesAsync(Channel.ToString());

        FeatureStatusDto dto = result.Value.Single(f => f.FeatureKey == "custom_code");
        dto.IsEnabled.Should().BeTrue("the channel's own opt-in row is on");
        dto.Entitled.Should()
            .BeFalse("the tier gate is not satisfied — the entitlement gate wins over opt-in");
        dto.EntitlementReason.Should().Be(FeatureEntitlementReason.RequiresTier);
        dto.RequiredTier.Should().Be("pro");
    }

    [Fact]
    public async Task GetFeatures_reports_entitled_when_a_governing_flag_is_enabled()
    {
        (FeatureService sut, _, IFeatureFlagService flags) = Build();
        flags
            .EvaluateAsync("custom_code", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new FeatureFlagEvaluation(true, true, null, null));

        Result<List<FeatureStatusDto>> result = await sut.GetFeaturesAsync(Channel.ToString());

        FeatureStatusDto dto = result.Value.Single(f => f.FeatureKey == "custom_code");
        dto.Entitled.Should().BeTrue();
        dto.EntitlementReason.Should().BeNull();
        dto.RequiredTier.Should().BeNull();
    }

    [Fact]
    public async Task GetFeatures_default_on_feature_reports_not_entitled_when_its_gate_excludes_the_channel()
    {
        // use_7tv defaults ON (no row), so IsEnabled is true — yet a deployment-mode gate can still deny it. The
        // client must see IsEnabled=true (opt-in) AND Entitled=false (gate) to hide the feature correctly.
        (FeatureService sut, _, IFeatureFlagService flags) = Build();
        flags
            .EvaluateAsync("use_7tv", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(
                new FeatureFlagEvaluation(true, false, FeatureEntitlementReason.Deployment, null)
            );

        Result<List<FeatureStatusDto>> result = await sut.GetFeaturesAsync(Channel.ToString());

        FeatureStatusDto dto = result.Value.Single(f => f.FeatureKey == "use_7tv");
        dto.IsEnabled.Should().BeTrue("use_7tv defaults ON when no row exists");
        dto.Entitled.Should().BeFalse("the deployment-mode gate excludes it");
        dto.EntitlementReason.Should().Be(FeatureEntitlementReason.Deployment);
    }

    [Fact]
    public async Task Toggling_ON_a_non_entitled_feature_fails_NOT_ENTITLED_and_writes_no_row()
    {
        FeatureServiceTestDbContext db = FeatureServiceTestDbContext.New();
        (FeatureService sut, RecordingEventBus bus, IFeatureFlagService flags) = BuildWith(db);
        flags
            .EvaluateAsync("custom_code", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(
                new FeatureFlagEvaluation(true, false, FeatureEntitlementReason.RequiresTier, "pro")
            );

        // custom_code defaults OFF, so this toggle targets ENABLE — which the gate must refuse.
        Result<FeatureStatusDto> result = await sut.ToggleFeatureAsync(
            Channel.ToString(),
            "custom_code"
        );

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NOT_ENTITLED");
        bus.Published.Should().BeEmpty("a refused toggle changes nothing and emits no event");
        (
            await db.ChannelFeatures.AnyAsync(f =>
                f.BroadcasterId == Channel && f.FeatureKey == "custom_code"
            )
        )
            .Should()
            .BeFalse("no opt-in row is persisted for a feature the channel cannot enable");
    }

    [Fact]
    public async Task Toggling_OFF_is_allowed_even_when_the_feature_is_not_entitled()
    {
        // Revoking a feature you can no longer afford (e.g. after a downgrade) must always succeed.
        FeatureServiceTestDbContext db = FeatureServiceTestDbContext.New();
        db.ChannelFeatures.Add(
            new ChannelFeature
            {
                BroadcasterId = Channel,
                FeatureKey = "custom_code",
                IsEnabled = true,
            }
        );
        await db.SaveChangesAsync();
        (FeatureService sut, _, IFeatureFlagService flags) = BuildWith(db);
        flags
            .EvaluateAsync("custom_code", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(
                new FeatureFlagEvaluation(true, false, FeatureEntitlementReason.RequiresTier, "pro")
            );

        Result<FeatureStatusDto> result = await sut.ToggleFeatureAsync(
            Channel.ToString(),
            "custom_code"
        );

        result.IsSuccess.Should().BeTrue("turning a feature OFF is never gated by entitlement");
        result.Value.IsEnabled.Should().BeFalse();
        ChannelFeature row = await db.ChannelFeatures.SingleAsync(f =>
            f.BroadcasterId == Channel && f.FeatureKey == "custom_code"
        );
        row.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Toggling_ON_an_entitled_gated_feature_succeeds_and_reports_entitled()
    {
        FeatureServiceTestDbContext db = FeatureServiceTestDbContext.New();
        (FeatureService sut, _, IFeatureFlagService flags) = BuildWith(db);
        flags
            .EvaluateAsync("custom_code", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new FeatureFlagEvaluation(true, true, null, null));

        Result<FeatureStatusDto> result = await sut.ToggleFeatureAsync(
            Channel.ToString(),
            "custom_code"
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.IsEnabled.Should().BeTrue();
        result.Value.Entitled.Should().BeTrue();
        ChannelFeature row = await db.ChannelFeatures.SingleAsync(f =>
            f.BroadcasterId == Channel && f.FeatureKey == "custom_code"
        );
        row.IsEnabled.Should().BeTrue();
    }
}
