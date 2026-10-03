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
using Microsoft.Extensions.Time.Testing;
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.CustomCode.Enums;
using NomNomzBot.Domain.CustomCode.ValueObjects;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.CustomCode.Jint;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Widgets.Bundling;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// An error in a script names the line and column in the AUTHOR's source, not in the bundle the sandbox runs: the
/// bundler drops blank lines and comments, so the position travels through the bundle's source map.
/// </summary>
public sealed class ScriptErrorPositionTests
{
    private const string SourceWithGaps =
        "const a = 1;\n\n\n// c\n// d\nbot.send(a);\nthrow new Error(\"x\");\n";

    private sealed class SilentBridge : IScriptHostBridge
    {
        public HostImportDelegate Resolve(string capabilityKey) => (_, _, _) => null;
    }

    private static ScriptExecutionRequest RequestFor(string js) =>
        new(
            "exec-1",
            js,
            "hash",
            new("u1", "User", [], new Dictionary<string, string>()),
            ScriptResourceBudget.Baseline with
            {
                WallClockMs = 30_000,
            }
        );

    private static ScriptCapabilityGrant GrantChat() =>
        new(Guid.NewGuid(), [new("chat.send", "tos", "ff", true)]);

    private static async Task<string> BundleAsync(string source)
    {
        Result<string> bundle = await ScriptBundlers
            .Real()
            .BundleAsync(new Dictionary<string, string> { ["index.ts"] = source }, "index.ts");
        bundle.IsSuccess.Should().BeTrue(bundle.ErrorMessage);
        return bundle.Value;
    }

    private static async Task<ScriptExecutionOutcomeResult> RunBundleAsync(string source) =>
        (
            await new JintScriptExecutor().ExecuteAsync(
                RequestFor(await BundleAsync(source)),
                GrantChat(),
                new SilentBridge()
            )
        ).Value;

    [Fact]
    public async Task The_source_map_sends_a_bundle_line_back_to_the_authors_line()
    {
        string bundle = await BundleAsync(SourceWithGaps);
        string[] lines = bundle.Split('\n');
        int throwLine = Array.FindIndex(lines, l => l.Contains("throw new Error")) + 1;

        ScriptSourcePosition? position = ScriptSourceMap.FromBundle(bundle)!.Map(throwLine, 3);

        throwLine.Should().BeLessThan(7, "the bundle drops the blank lines and the comments");
        position.Should().Be(new ScriptSourcePosition("index.ts", 7, 1));
    }

    [Fact]
    public async Task A_runtime_throw_reports_the_line_in_the_authors_source()
    {
        ScriptExecutionOutcomeResult outcome = await RunBundleAsync(SourceWithGaps);

        outcome.Outcome.Should().Be(ScriptExecutionOutcome.Faulted);
        outcome.ErrorPosition.Should().NotBeNull();
        outcome.ErrorPosition!.Line.Should().Be(7);
    }

    [Fact]
    public async Task A_runtime_throw_on_line_two_reports_line_two()
    {
        ScriptExecutionOutcomeResult outcome = await RunBundleAsync(
            "const a = 1;\nthrow new Error(\"x\");\n"
        );

        outcome.ErrorPosition!.Line.Should().Be(2);
    }

    [Fact]
    public async Task A_missing_argument_inside_the_sandbox_points_at_the_authors_call()
    {
        ScriptExecutionOutcomeResult outcome = await RunBundleAsync(
            "const a = 1;\n// c\nnnz.api.chat.send();\n"
        );

        outcome.ErrorMessage.Should().Contain("chat.send needs a message");
        outcome.ErrorPosition!.Line.Should().Be(3);
    }

    [Fact]
    public async Task A_syntax_error_on_line_three_names_line_three_and_its_column()
    {
        Result<ScriptCompilation> compiled = await new JintScriptExecutor().CompileAsync(
            "let a = 1;\nlet b = 2;\nlet c = ;\n"
        );

        compiled.IsFailure.Should().BeTrue();
        ScriptSourcePosition position = compiled
            .ErrorData.Should()
            .BeOfType<ScriptSourcePosition>()
            .Subject;
        position.Line.Should().Be(3);
        position.Column.Should().Be(9);
    }

    [Fact]
    public async Task A_build_error_carries_the_file_line_and_column()
    {
        Result<string> bundle = await ScriptBundlers
            .Real()
            .BundleAsync(
                new Dictionary<string, string>
                {
                    ["index.ts"] =
                        "const a = 1;\nimport SCENES from './scenes';\nbot.send(SCENES);\n",
                },
                "index.ts"
            );

        bundle.IsFailure.Should().BeTrue();
        List<ScriptBuildError> errors = bundle
            .ErrorData.Should()
            .BeAssignableTo<IEnumerable<ScriptBuildError>>()
            .Subject.ToList();
        errors.Should().ContainSingle();
        errors[0].Position.Should().Be(new ScriptSourcePosition("index.ts", 2, 20));
    }

    [Fact]
    public async Task Creating_a_project_with_a_missing_import_stores_a_build_error_with_its_line()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        tenantService.BroadcasterId.Returns(Guid.NewGuid());
        CodeScriptService sut = new(
            db,
            tenantService,
            new JintScriptExecutor(),
            ScriptBundlers.Real(),
            new RecordingEventBus(),
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero)),
            new WidgetDependencyAllowlist()
        );
        Result<CodeScriptDetailDto> created = await sut.CreateProjectAsync(
            "pos",
            "desc",
            new(
                new Dictionary<string, string>
                {
                    ["index.ts"] =
                        "const a = 1;\nimport SCENES from './scenes';\nbot.send(SCENES);\n",
                },
                new("index.ts", "script", "typescript", [])
            )
        );

        created.IsFailure.Should().BeTrue();
        string? json = db.CodeScriptVersions.Single().ValidationErrorsJson;
        List<ScriptValidationError> errors = JsonConvert.DeserializeObject<
            List<ScriptValidationError>
        >(json ?? "[]")!;
        ScriptValidationError error = errors.Should().ContainSingle().Subject;
        error.Code.Should().Be("build");
        error.Line.Should().Be(2);
        error.Column.Should().Be(20);
    }
}
