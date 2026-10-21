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
using NomNomzBot.Api.Configuration;

namespace NomNomzBot.Api.Tests.Configuration;

public sealed class StaticAssetCachePolicyTests
{
    [Theory]
    [InlineData("/index.html")]
    [InlineData("/INDEX.HTML")]
    public void TheShellDocument_IsNeverStored_SoAReloadRunsTheDeployedBuild(string path)
    {
        StaticAssetCachePolicy.For(path).Should().Be(StaticAssetCachePolicy.EntryPoint);
    }

    [Theory]
    [InlineData("/assets/index-3KohvHdj.js")]
    [InlineData("/assets/DashboardLayout-CB_WYf8P.js")]
    [InlineData("/assets/index-Dk3f2a1b.css")]
    [InlineData("/assets/inter-latin-wght-normal-C2S99t-D.woff2")]
    public void TheHashedBuildFiles_AreImmutableAndEdgeCacheable(string path)
    {
        string policy = StaticAssetCachePolicy.For(path);

        policy.Should().Be(StaticAssetCachePolicy.Immutable);
        policy.Should().Contain("public", "the CDN must be allowed to serve these from the edge");
        policy.Should().Contain("immutable").And.Contain("max-age=31536000");
        policy.Should().NotContain("no-cache");
    }

    [Theory]
    [InlineData("/sw.js")]
    [InlineData("/workbox-5bf5ec49.js")]
    [InlineData("/manifest.webmanifest")]
    [InlineData("/icon.svg")]
    [InlineData("/pwa-192x192.png")]
    [InlineData("/favicon.ico")]
    public void FilesUnderAStableName_StayRevalidatedButBecomeEdgeCacheable(string path)
    {
        string policy = StaticAssetCachePolicy.For(path);

        policy.Should().Be(StaticAssetCachePolicy.Revalidated);
        policy
            .Should()
            .Contain("public", "the edge may hold it and answer 304 instead of reaching origin");
        policy.Should().Contain("must-revalidate");
        policy.Should().NotContain("immutable");
    }

    [Fact]
    public void TheServiceWorker_IsNotImmutable_SoADeployReachesInstalledBrowsers()
    {
        StaticAssetCachePolicy
            .For("/sw.js")
            .Should()
            .NotBe(
                StaticAssetCachePolicy.Immutable,
                "a service worker cached without revalidation pins every install to an old build"
            );
    }
}
