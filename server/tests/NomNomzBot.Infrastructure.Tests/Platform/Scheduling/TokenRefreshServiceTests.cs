// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Infrastructure.Platform.Scheduling;
using NomNomzBot.Infrastructure.Tests.Platform.Deployment;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Scheduling;

/// <summary>
/// The periodic Twitch token sweep runs on one colour of a blue/green deploy at a time: the refresh gate is
/// per process, so two colours sweeping together redeem the same refresh tokens side by side.
/// </summary>
public sealed class TokenRefreshServiceTests
{
    [Fact]
    public async Task Only_the_lease_holding_colour_sweeps_and_the_successor_takes_over_after_the_handover()
    {
        TwoColourDeployment deployment = await TwoColourDeployment.StartAsync();
        ITwitchAuthService blueAuth = Substitute.For<ITwitchAuthService>();
        TokenRefreshService blue = BuildColour(blueAuth, deployment.Blue);
        ITwitchAuthService greenAuth = Substitute.For<ITwitchAuthService>();
        TokenRefreshService green = BuildColour(greenAuth, deployment.Green);

        await blue.RefreshOnceAsync(CancellationToken.None);
        await green.RefreshOnceAsync(CancellationToken.None);

        await blueAuth.Received(1).RefreshExpiringTokensAsync(Arg.Any<CancellationToken>());
        await greenAuth.DidNotReceive().RefreshExpiringTokensAsync(Arg.Any<CancellationToken>());

        await deployment.HandOverAsync();
        await blue.RefreshOnceAsync(CancellationToken.None);
        await green.RefreshOnceAsync(CancellationToken.None);

        // The outgoing colour stopped sweeping with its lease; the successor sweeps now.
        await blueAuth.Received(1).RefreshExpiringTokensAsync(Arg.Any<CancellationToken>());
        await greenAuth.Received(1).RefreshExpiringTokensAsync(Arg.Any<CancellationToken>());

        await deployment.StopAsync();
    }

    private static TokenRefreshService BuildColour(
        ITwitchAuthService twitchAuth,
        IActiveInstanceGate instanceGate
    )
    {
        ServiceCollection services = new();
        services.AddSingleton(twitchAuth);
        services.AddSingleton(instanceGate);
        return new(services.BuildServiceProvider(), NullLogger<TokenRefreshService>.Instance);
    }
}
