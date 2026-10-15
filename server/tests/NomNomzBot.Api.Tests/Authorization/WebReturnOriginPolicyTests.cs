// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NomNomzBot.Api.Authorization;

namespace NomNomzBot.Api.Tests.Authorization;

/// <summary>
/// The Vue dashboard served at vue.nomnomz.bot signs in through the dev bot at dev.nomnomz.bot. The sign-in
/// returns to the Vue origin only when that origin is listed, so the SHIPPED lists (the built-in default and
/// appsettings.json) must carry it. Left out, the flow fell back to the base URL and landed on the wrong site.
/// </summary>
public sealed class WebReturnOriginPolicyTests
{
    private const string VueOrigin = "https://vue.nomnomz.bot";

    private static IHostEnvironment Production()
    {
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);
        return environment;
    }

    [Fact]
    public void BuiltInDefaultList_TrustsTheVueDashboardOrigin()
    {
        IConfiguration empty = new ConfigurationBuilder().Build();

        bool listed = WebReturnOriginPolicy.IsListed(VueOrigin, empty, Production());

        Assert.True(listed);
    }

    [Fact]
    public void ShippedAppsettings_TrustsTheVueDashboardOrigin()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        IConfiguration shipped = new ConfigurationBuilder()
            .AddJsonFile(path, optional: false)
            .Build();

        bool listed = WebReturnOriginPolicy.IsListed(VueOrigin, shipped, Production());

        Assert.True(listed);
    }

    [Fact]
    public void AnUnlistedOrigin_IsStillRefused()
    {
        IConfiguration empty = new ConfigurationBuilder().Build();

        bool listed = WebReturnOriginPolicy.IsListed("https://evil.example", empty, Production());

        Assert.False(listed);
    }
}
