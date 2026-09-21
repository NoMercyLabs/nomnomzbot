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
    [InlineData("composeApp.js")]
    [InlineData("index.html")]
    [InlineData("INDEX.HTML")]
    public void EntryPoints_AreNeverStored_SoAReloadRunsTheDeployedBuild(string fileName)
    {
        StaticAssetCachePolicy.For(fileName).Should().Be(StaticAssetCachePolicy.EntryPoint);
    }

    [Theory]
    [InlineData("bccfa839aa4b38489c76.wasm")]
    [InlineData("noto_sans_sc.ttf")]
    [InlineData("twemoji_color.ttf")]
    [InlineData("inter.woff2")]
    public void TheBulkyContentStableAssets_AreImmutableAndEdgeCacheable(string fileName)
    {
        string policy = StaticAssetCachePolicy.For(fileName);

        policy.Should().Be(StaticAssetCachePolicy.Immutable);
        policy.Should().Contain("public", "the CDN must be allowed to serve these from the edge");
        policy.Should().Contain("immutable").And.Contain("max-age=31536000");
        policy.Should().NotContain("no-cache");
    }

    [Theory]
    [InlineData("activity.svg")]
    [InlineData("strings.cvr")]
    [InlineData("composeApp.js.map")]
    public void BuildVaryingAssets_StayRevalidatedButBecomeEdgeCacheable(string fileName)
    {
        string policy = StaticAssetCachePolicy.For(fileName);

        policy.Should().Be(StaticAssetCachePolicy.Revalidated);
        policy
            .Should()
            .Contain("public", "the edge may hold it and answer 304 instead of reaching origin");
        policy.Should().Contain("must-revalidate");
        policy.Should().NotContain("immutable");
    }

    [Fact]
    public void TheEntryPointScriptIsNotTreatedAsAnImmutableAssetByItsExtension()
    {
        StaticAssetCachePolicy
            .For("composeApp.js")
            .Should()
            .NotBe(
                StaticAssetCachePolicy.Immutable,
                "a cached entry point pins the browser to an old build"
            );
    }
}
