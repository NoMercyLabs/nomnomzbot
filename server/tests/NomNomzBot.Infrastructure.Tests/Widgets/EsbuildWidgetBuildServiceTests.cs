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
/// Proves the widget compile boundary's behaviour: vanilla passes through with a correct cache-bust hash and never
/// shells out; a framework build materializes the project to a temp dir and invokes esbuild from the manifest entry;
/// esbuild's failures and absence surface as honest, coded <see cref="Result"/> failures.
/// </summary>
public sealed class EsbuildWidgetBuildServiceTests
{
    private static EsbuildWidgetBuildService Build(IProcessRunner runner) =>
        Build(runner, Substitute.For<IVueSfcCompiler>());

    private static EsbuildWidgetBuildService Build(IProcessRunner runner, IVueSfcCompiler vue) =>
        new(
            runner,
            vue,
            new WidgetDependencyAllowlist(),
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
            WidgetBuildInput.SingleFile("vanilla", source)
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

        string hashA1 = (await service.BuildAsync(WidgetBuildInput.SingleFile("vanilla", "A")))
            .Value
            .ContentHash;
        string hashA2 = (await service.BuildAsync(WidgetBuildInput.SingleFile("VANILLA", "A")))
            .Value
            .ContentHash;
        string hashB = (await service.BuildAsync(WidgetBuildInput.SingleFile("vanilla", "B")))
            .Value
            .ContentHash;

        hashA1.Should().Be(hashA2); // same source -> same cache-bust key (framework casing ignored)
        hashA1.Should().NotBe(hashB); // different source -> different key
    }

    [Fact]
    public async Task React_materializes_the_entry_and_invokes_esbuild_bundling_the_returned_bundle()
    {
        const string bundle = "(()=>{var e=1;})();";
        const string warnings = "▲ [WARNING] Unused import";
        ProcessRunRequest? captured = null;
        IProcessRunner runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Do<ProcessRunRequest>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(new ProcessRunResult(true, 0, bundle, warnings));
        EsbuildWidgetBuildService service = Build(runner);

        Result<WidgetBuildOutput> result = await service.BuildAsync(
            WidgetBuildInput.SingleFile("react", "export default () => <div/>;")
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.CompiledBundle.Should().Be(bundle);
        result.Value.ContentHash.Should().Be(Sha256Hex(bundle));
        result.Value.BuildLog.Should().Be(warnings); // esbuild stderr carries warnings even on success

        // The project was materialized to a temp working dir; esbuild is invoked from the manifest entry file (no
        // stdin — the source is on disk now so cross-file imports resolve).
        captured.Should().NotBeNull();
        captured!.FileName.Should().Be("esbuild");
        captured.StandardInput.Should().BeNull();
        captured.WorkingDirectory.Should().NotBeNullOrEmpty();
        captured.Arguments.Should().Contain("--bundle");
        captured.Arguments.Should().Contain("--format=iife");
        captured.Arguments.Should().Contain("index.tsx"); // the single-file react entry
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
            WidgetBuildInput.SingleFile("react", "broken(")
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
            WidgetBuildInput.SingleFile("react", "x")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("WIDGET_BUILD_TOOL_UNAVAILABLE");
        result.ErrorMessage.Should().Contain("esbuild");
    }

    [Theory]
    [InlineData("svelte")]
    public async Task Plugin_frameworks_fail_honestly_rather_than_mis_compiling(string framework)
    {
        // Svelte still needs the plugin-based build (Vue is supported via IVueSfcCompiler + esbuild).
        IProcessRunner runner = Substitute.For<IProcessRunner>();
        EsbuildWidgetBuildService service = Build(runner);

        Result<WidgetBuildOutput> result = await service.BuildAsync(
            WidgetBuildInput.SingleFile(framework, "source")
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("WIDGET_FRAMEWORK_UNSUPPORTED");
        await runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>());
    }
}
