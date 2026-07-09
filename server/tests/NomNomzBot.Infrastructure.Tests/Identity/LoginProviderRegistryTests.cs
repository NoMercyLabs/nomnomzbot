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
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Platform;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;
using Xunit;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Behavioural proof for <see cref="LoginProviderRegistry"/> (platform-identity §3.2): Twitch is always on,
/// YouTube/Kick/Twitter are registered but light up only when BOTH their feature flag is on AND their OAuth app
/// credentials are configured, and enablement is resolved through a fresh scope so the singleton registry never
/// captures the scoped <see cref="IFeatureFlagService"/> / <see cref="ISystemCredentialsProvider"/>.
/// </summary>
public sealed class LoginProviderRegistryTests
{
    private static LoginProviderRegistry Build(
        IFeatureFlagService flags,
        ISystemCredentialsProvider? credentials = null
    )
    {
        ServiceCollection services = new();
        services.AddSingleton(flags);
        services.AddSingleton(credentials ?? Substitute.For<ISystemCredentialsProvider>());
        ServiceProvider provider = services.BuildServiceProvider();
        return new LoginProviderRegistry(provider.GetRequiredService<IServiceScopeFactory>());
    }

    [Fact]
    public void All_lists_every_registered_login_provider()
    {
        LoginProviderRegistry registry = Build(Substitute.For<IFeatureFlagService>());
        registry
            .All.Select(d => d.Key)
            .Should()
            .BeEquivalentTo(["twitch", "youtube", "kick", "twitter"]);
    }

    [Fact]
    public void Get_unknown_provider_fails_with_UNKNOWN_PROVIDER()
    {
        Result<LoginProviderDescriptor> result = Build(Substitute.For<IFeatureFlagService>())
            .Get("myspace");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("UNKNOWN_PROVIDER");
    }

    [Fact]
    public void Get_twitch_returns_the_always_on_descriptor()
    {
        Result<LoginProviderDescriptor> result = Build(Substitute.For<IFeatureFlagService>())
            .Get("twitch");

        result.IsSuccess.Should().BeTrue();
        result.Value.FeatureFlagKey.Should().BeEmpty();
        result.Value.SupportedFlows.Should().HaveFlag(LoginFlows.DeviceCode);
    }

    [Fact]
    public async Task EnabledAsync_includes_twitch_and_excludes_flagged_off_providers()
    {
        IFeatureFlagService flags = Substitute.For<IFeatureFlagService>();
        flags
            .IsEnabledForAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);

        IReadOnlyList<LoginProviderDescriptor> enabled = await Build(flags).EnabledAsync();

        enabled.Select(d => d.Key).Should().Equal("twitch");
    }

    [Fact]
    public async Task EnabledAsync_includes_a_flagged_on_provider_that_has_credentials()
    {
        IFeatureFlagService flags = Substitute.For<IFeatureFlagService>();
        flags
            .IsEnabledForAsync("use_youtube_login", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        flags
            .IsEnabledForAsync("use_kick_login", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // YouTube's flag is on AND its OAuth app is configured — so the button is safe to show.
        ISystemCredentialsProvider credentials = Substitute.For<ISystemCredentialsProvider>();
        credentials
            .GetClientIdAsync("youtube", Arg.Any<CancellationToken>())
            .Returns("yt-client-id");

        IReadOnlyList<LoginProviderDescriptor> enabled = await Build(flags, credentials)
            .EnabledAsync();

        enabled.Select(d => d.Key).Should().BeEquivalentTo(["twitch", "youtube"]);
    }

    [Fact]
    public async Task EnabledAsync_excludes_a_flagged_on_provider_that_has_no_credentials()
    {
        // Every provider's flag is on, but none of the flag-gated ones have OAuth credentials configured.
        IFeatureFlagService flags = Substitute.For<IFeatureFlagService>();
        flags
            .IsEnabledForAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        ISystemCredentialsProvider credentials = Substitute.For<ISystemCredentialsProvider>();
        credentials
            .GetClientIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        IReadOnlyList<LoginProviderDescriptor> enabled = await Build(flags, credentials)
            .EnabledAsync();

        // Only Twitch survives (always-on; its client id is a boot credential) — no dead buttons for the rest.
        enabled.Select(d => d.Key).Should().Equal("twitch");
    }
}
