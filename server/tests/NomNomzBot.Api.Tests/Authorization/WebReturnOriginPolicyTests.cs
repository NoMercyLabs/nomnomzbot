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
using NomNomzBot.Api.Authorization;
using NSubstitute;

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

    /// <summary>The Vue page navigates the browser straight to the bot, so the request host is the bot's own.</summary>
    private static HttpRequest DirectSignIn(string? referer)
    {
        DefaultHttpContext context = new();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("dev.nomnomz.bot");
        if (referer is not null)
            context.Request.Headers.Referer = referer;
        return context.Request;
    }

    private static IConfiguration DevBot() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["App:BaseUrl"] = "https://dev.nomnomz.bot" }
            )
            .Build();

    [Fact]
    public void ADirectSignInFromTheVuePage_ReturnsToTheVueOrigin()
    {
        string? origin = WebReturnOriginPolicy.Capture(
            DirectSignIn("https://vue.nomnomz.bot/login"),
            DevBot(),
            Production()
        );

        Assert.Equal(VueOrigin, origin);
    }

    [Fact]
    public void ADirectSignInFromAnUnlistedPage_ReturnsToTheBaseUrl()
    {
        string? origin = WebReturnOriginPolicy.Capture(
            DirectSignIn("https://evil.example/login"),
            DevBot(),
            Production()
        );

        Assert.Null(origin);
    }
}
