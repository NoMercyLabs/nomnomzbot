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
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;
using Xunit;

namespace NomNomzBot.Api.Tests.Requirements.Auth;

/// <summary>
/// REQUIREMENT (owner-settled 2026-07-20): every platform with a live-stream service, plus Twitter, is a
/// bot-login provider. Each MUST be wired to a working login flow — a device (<see cref="ILoginIdentityProvider"/>)
/// or auth-code (<see cref="IAuthCodeLoginProvider"/>) impl keyed to it — AND carry a descriptor in the registry.
/// A declared provider with no flow/descriptor cannot sign the bot in. These tests DEMAND that requirement against
/// the real service container; they are not a snapshot of current wiring, so a red here is a real gap to close.
/// </summary>
public sealed class LoginProviderCoverageTests : IClassFixture<DiHostFixture>
{
    private readonly DiHostFixture _host;

    public LoginProviderCoverageTests(DiHostFixture host) => _host = host;

    public static readonly string[] RequiredLoginProviders =
    [
        AuthEnums.LoginProvider.Twitch,
        AuthEnums.LoginProvider.Kick,
        AuthEnums.LoginProvider.YouTube,
        AuthEnums.LoginProvider.Twitter,
    ];

    [Fact]
    public void Every_declared_login_provider_has_a_registered_login_flow()
    {
        using IServiceScope scope = _host.Services.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        HashSet<string> wired = sp.GetServices<ILoginIdentityProvider>()
            .Select(p => p.Key)
            .Concat(sp.GetServices<IAuthCodeLoginProvider>().Select(p => p.Key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<string> missing = RequiredLoginProviders.Where(p => !wired.Contains(p)).ToList();

        missing
            .Should()
            .BeEmpty(
                "every declared bot-login platform needs a device or auth-code login flow; "
                    + $"wired flows are [{string.Join(", ", wired)}]"
            );
    }

    [Fact]
    public void Every_declared_login_provider_has_a_registry_descriptor()
    {
        ILoginProviderRegistry registry =
            _host.Services.GetRequiredService<ILoginProviderRegistry>();

        List<string> missing = RequiredLoginProviders
            .Where(p => registry.Get(p).IsFailure)
            .ToList();

        missing
            .Should()
            .BeEmpty(
                "every declared bot-login platform needs a LoginProviderDescriptor; "
                    + $"registered are [{string.Join(", ", registry.All.Select(d => d.Key))}]"
            );
    }
}
