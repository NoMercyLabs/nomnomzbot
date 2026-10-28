// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
