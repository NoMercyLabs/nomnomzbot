// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure.Widgets.Bundling;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Proves the widget compile boundary's behaviour: vanilla passes through with a correct cache-bust hash and
/// never shells out; a framework build invokes esbuild with the right args and returns its bundle hashed;
/// esbuild's failures and absence surface as honest, coded <see cref="Result"/> failures.
/// </summary>
public sealed class EsbuildWidgetBuildServiceTests
{
    private static EsbuildWidgetBuildService Build(IProcessRunner runner) =>
        new(
            runner,
            new ConfigurationBuilder().Build(),
            NullLogger<EsbuildWidgetBuildService>.Instance
        );

    private static string Sha256Hex(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    [Fact]
    public async Task Vanilla_passes_source_through_unchanged_and_never_shells_out()
    {
        IProcessRunner runner = Substitute.For<IProcessRunner>();
        EsbuildWidgetBuildService service = Build(runner);
        const string source =
            "<div class=\"alert\">{{name}} followed!</div><script>NomNomz.on('follow')</script>";

        Result<WidgetBuildOutput> result = await service.BuildAsync(
            new WidgetBuildInput("vanilla", source)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.CompiledBundle.Should().Be(source);
        result.Value.ContentHash.Should().Be(Sha256Hex(source));
        result.Value.ContentHash.Should().MatchRegex("^[0-9a-f]{64}$");
        // Vanilla is browser-ready — the build tool is never invoked.
        await runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Vanilla_hash_is_deterministic_and_distinguishes_sources()
    {
        EsbuildWidgetBuildService service = Build(Substitute.For<IProcessRunner>());

        string hashA1 = (await service.BuildAsync(new WidgetBuildInput("vanilla", "A")))
            .Value
            .ContentHash;
        string hashA2 = (await service.BuildAsync(new WidgetBuildInput("VANILLA", "A")))
            .Value
            .ContentHash;
        string hashB = (await service.BuildAsync(new WidgetBuildInput("vanilla", "B")))
            .Value
            .ContentHash;

        hashA1.Should().Be(hashA2); // same source -> same cache-bust key (framework casing ignored)
        hashA1.Should().NotBe(hashB); // different source -> different key
    }

    [Fact]
    public async Task React_invokes_esbuild_with_bundle_args_and_hashes_the_returned_bundle()
    {
        const string bundle = "(()=>{var e=1;})();";
        const string warnings = "▲ [WARNING] Unused import";
        IProcessRunner runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessRunResult(true, 0, bundle, warnings));
        EsbuildWidgetBuildService service = Build(runner);

        Result<WidgetBuildOutput> result = await service.BuildAsync(
            new WidgetBuildInput("react", "export default () => <div/>;")
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.CompiledBundle.Should().Be(bundle);
        result.Value.ContentHash.Should().Be(Sha256Hex(bundle));
        result.Value.BuildLog.Should().Be(warnings); // esbuild stderr carries warnings even on success

        await runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRunRequest>(r =>
                    r.FileName == "esbuild"
                    && r.StandardInput == "export default () => <div/>;"
                    && r.Arguments.Contains("--bundle")
                    && r.Arguments.Contains("--format=iife")
                    && r.Arguments.Contains("--loader=tsx")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task React_build_failure_surfaces_esbuild_stderr()
    {
        IProcessRunner runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new ProcessRunResult(
                    true,
                    1,
                    string.Empty,
                    "✘ [ERROR] Expected \";\" but found \"}\""
                )
            );
        EsbuildWidgetBuildService service = Build(runner);

        Result<WidgetBuildOutput> result = await service.BuildAsync(
            new WidgetBuildInput("react", "broken(")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("WIDGET_BUILD_FAILED");
        result.ErrorMessage.Should().Contain("Expected");
    }

    [Fact]
    public async Task Missing_esbuild_binary_returns_an_install_hint()
    {
        IProcessRunner runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessRunResult(false, -1, string.Empty, "No such file"));
        EsbuildWidgetBuildService service = Build(runner);

        Result<WidgetBuildOutput> result = await service.BuildAsync(
            new WidgetBuildInput("react", "x")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("WIDGET_BUILD_TOOL_UNAVAILABLE");
        result.ErrorMessage.Should().Contain("esbuild");
    }

    [Theory]
    [InlineData("vue")]
    [InlineData("svelte")]
    public async Task Plugin_frameworks_fail_honestly_rather_than_mis_compiling(string framework)
    {
        IProcessRunner runner = Substitute.For<IProcessRunner>();
        EsbuildWidgetBuildService service = Build(runner);

        Result<WidgetBuildOutput> result = await service.BuildAsync(
            new WidgetBuildInput(framework, "source")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("WIDGET_FRAMEWORK_UNSUPPORTED");
        await runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>());
    }
}
