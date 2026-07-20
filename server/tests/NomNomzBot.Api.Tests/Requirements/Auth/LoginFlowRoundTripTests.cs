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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;
using Xunit;

namespace NomNomzBot.Api.Tests.Requirements.Auth;

/// <summary>
/// REQUIREMENT (platform-identity §3.2 / §10.3): the login handshake is uniform — every declared login provider
/// runs through the SAME generic seam, a device-flow <see cref="ILoginIdentityProvider"/> or an auth-code + PKCE
/// <see cref="IAuthCodeLoginProvider"/> keyed to it. A provider's <c>LoginProviderDescriptor.SupportedFlows</c>
/// is a promise to the client; each promised generic flow MUST be backed by a matching impl, or the client can
/// pick a handshake that dead-ends (501 / config error) instead of returning a real device-start / authorize URL.
/// These tests DEMAND that uniform round-trip against the real container; they are not a snapshot of today's
/// wiring, so a red here is a real gap to close — most notably Twitch, whose login still lives in the bespoke
/// <see cref="IAuthService"/> path rather than the generic seam.
/// </summary>
public sealed class LoginFlowRoundTripTests : IClassFixture<DiHostFixture>
{
    private readonly DiHostFixture _host;

    public LoginFlowRoundTripTests(DiHostFixture host) => _host = host;

    /// <summary>
    /// Every declared login provider (streaming platforms + login-only Twitter/X). Each is a bot-login provider
    /// per <c>AuthEnums.LoginProvider</c> and MUST be able to start its declared handshake through the generic seam.
    /// </summary>
    [Theory]
    [InlineData(AuthEnums.LoginProvider.Twitch)]
    [InlineData(AuthEnums.LoginProvider.Kick)]
    [InlineData(AuthEnums.LoginProvider.YouTube)]
    [InlineData(AuthEnums.LoginProvider.Twitter)]
    public void Every_login_provider_backs_its_declared_generic_flows_with_a_matching_impl(
        string provider
    )
    {
        using IServiceScope scope = _host.Services.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        ILoginProviderRegistry registry = sp.GetRequiredService<ILoginProviderRegistry>();
        Result<LoginProviderDescriptor> descriptor = registry.Get(provider);
        descriptor
            .IsSuccess.Should()
            .BeTrue($"login provider '{provider}' must carry a LoginProviderDescriptor");

        LoginFlows flows = descriptor.Value.SupportedFlows;

        HashSet<string> deviceKeys = sp.GetServices<ILoginIdentityProvider>()
            .Select(p => p.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> authCodeKeys = sp.GetServices<IAuthCodeLoginProvider>()
            .Select(p => p.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // The two flows the GENERIC seam owns. Plain redirect AuthCode is Twitch's bespoke IAuthService path and
        // is intentionally out of the generic-impl requirement, so it is not asserted here.
        List<string> unfulfilled = [];
        if (flows.HasFlag(LoginFlows.DeviceCode) && !deviceKeys.Contains(provider))
            unfulfilled.Add("device_code → ILoginIdentityProvider");
        if (flows.HasFlag(LoginFlows.AuthCodePkce) && !authCodeKeys.Contains(provider))
            unfulfilled.Add("auth_code_pkce → IAuthCodeLoginProvider");

        unfulfilled
            .Should()
            .BeEmpty(
                $"'{provider}' advertises flows [{flows}] to the client, so each generic flow needs a matching "
                    + $"impl to return a real device-start / authorize URL rather than a 501. Unfulfilled: "
                    + $"[{string.Join(", ", unfulfilled)}]. Registered device impls=[{string.Join(", ", deviceKeys)}], "
                    + $"auth-code impls=[{string.Join(", ", authCodeKeys)}]"
            );
    }

    /// <summary>
    /// The headline gap: Twitch is the shipped, always-on login provider, yet — unlike youtube/kick/twitter — it
    /// has NO generic <see cref="ILoginIdentityProvider"/>/<see cref="IAuthCodeLoginProvider"/> impl. Its device
    /// login is served only by the bespoke <see cref="IAuthService"/> path, so the uniform seam the spec mandates
    /// is incomplete. This is the known RED called out in the login-provider coverage brief.
    /// </summary>
    [Fact]
    public void Twitch_login_runs_through_the_same_generic_seam_as_every_other_provider()
    {
        using IServiceScope scope = _host.Services.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        bool hasDeviceImpl = sp.GetServices<ILoginIdentityProvider>()
            .Any(p =>
                string.Equals(
                    p.Key,
                    AuthEnums.LoginProvider.Twitch,
                    StringComparison.OrdinalIgnoreCase
                )
            );
        bool hasAuthCodeImpl = sp.GetServices<IAuthCodeLoginProvider>()
            .Any(p =>
                string.Equals(
                    p.Key,
                    AuthEnums.LoginProvider.Twitch,
                    StringComparison.OrdinalIgnoreCase
                )
            );

        (hasDeviceImpl || hasAuthCodeImpl)
            .Should()
            .BeTrue(
                "Twitch login should plug into the generic ILoginIdentityProvider / IAuthCodeLoginProvider seam "
                    + "like youtube/kick/twitter (platform-identity §3.2 uniform seam), not live only in the "
                    + "bespoke IAuthService path — otherwise the login round-trip is not uniform across providers"
            );
    }
}
