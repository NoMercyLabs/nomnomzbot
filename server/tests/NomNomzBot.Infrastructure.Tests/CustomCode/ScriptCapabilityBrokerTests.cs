// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Linq;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Platform;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
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
        IFeatureFlagService featureFlags = Substitute.For<IFeatureFlagService>();
        featureFlags
            .IsEnabledForAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(featureEnabled);
        return new ScriptCapabilityBroker(featureFlags);
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
}
