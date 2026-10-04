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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.Widgets.Bundling;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

// The editor creates `index.ts` and its SDK types invite type annotations, but the sandbox ran the source as plain
// JavaScript: any `: string` was a save-time syntax error. The bundler strips types and pulls in imported files.
public sealed class EsbuildScriptBundlerTests
{
    [Fact]
    public async Task Types_are_stripped_and_imported_files_are_bundled_into_one_program()
    {
        Dictionary<string, string> files = new()
        {
            ["index.ts"] =
                "import { greet, type Greeting } from './lib/greet';\n"
                + "const g: Greeting = { name: 'kitte' };\n"
                + "bot.send(greet(g));\n",
            ["lib/greet.ts"] =
                "export interface Greeting { name: string }\n"
                + "export function greet(g: Greeting): string { return `hi ${g.name}`; }\n",
        };

        Result<string> bundle = await ScriptBundlers.Real().BundleAsync(files, "index.ts");

        bundle.IsSuccess.Should().BeTrue(bundle.ErrorMessage);
        bundle.Value.Should().NotContain("interface").And.NotContain(": Greeting");
        bundle.Value.Should().NotContain("import ");
        bundle.Value.Should().Contain("// lib/greet.ts").And.Contain("function greet(");
    }

    [Fact]
    public async Task A_missing_import_names_the_file_line_and_column()
    {
        Dictionary<string, string> files = new()
        {
            ["index.ts"] = "const a = 1;\nimport SCENES from './scenes';\nbot.send(SCENES);\n",
        };

        Result<string> bundle = await ScriptBundlers.Real().BundleAsync(files, "index.ts");

        bundle.IsFailure.Should().BeTrue();
        bundle.ErrorCode.Should().Be("SCRIPT_BUILD_FAILED");
        bundle.ErrorMessage.Should().Be("index.ts:2:20: Could not resolve \"./scenes\"");
    }

    [Fact]
    public async Task A_namespace_member_the_file_does_not_export_is_a_build_error_at_the_use()
    {
        Dictionary<string, string> files = new()
        {
            ["index.ts"] = "import * as h from './helpers';\nh.missing();\n",
            ["helpers.ts"] = "export const present = 1;\n",
        };

        Result<string> bundle = await ScriptBundlers.Real().BundleAsync(files, "index.ts");

        bundle.IsFailure.Should().BeTrue("the output would call (void 0)");
        bundle.ErrorCode.Should().Be("SCRIPT_BUILD_FAILED");
        List<ScriptBuildError> errors = bundle
            .ErrorData.Should()
            .BeAssignableTo<IEnumerable<ScriptBuildError>>()
            .Subject.ToList();
        errors.Should().ContainSingle();
        errors[0].Position.Should().Be(new ScriptSourcePosition("index.ts", 2, 3));
        errors[0].Message.Should().Contain("\"missing\"").And.Contain("helpers.ts");
    }

    [Fact]
    public async Task A_name_imported_from_a_file_with_no_exports_is_a_build_error_at_the_import()
    {
        Dictionary<string, string> files = new()
        {
            ["index.ts"] = "import { a } from './plain';\nbot.send(a);\n",
            ["plain.ts"] = "bot.send('side effect only');\n",
        };

        Result<string> bundle = await ScriptBundlers.Real().BundleAsync(files, "index.ts");

        bundle.IsFailure.Should().BeTrue("the output would send undefined");
        bundle.ErrorCode.Should().Be("SCRIPT_BUILD_FAILED");
        List<ScriptBuildError> errors = bundle
            .ErrorData.Should()
            .BeAssignableTo<IEnumerable<ScriptBuildError>>()
            .Subject.ToList();
        errors.Should().ContainSingle();
        errors[0].Position.Should().Be(new ScriptSourcePosition("index.ts", 1, 10));
        errors[0].Message.Should().Contain("plain.ts").And.Contain("no exports");
    }

    [Fact]
    public async Task A_named_import_the_file_does_not_export_is_a_build_error_at_the_import()
    {
        Dictionary<string, string> files = new()
        {
            ["index.ts"] = "import { nope } from './helpers';\nnope();\n",
            ["helpers.ts"] = "export const present = 1;\n",
        };

        Result<string> bundle = await ScriptBundlers.Real().BundleAsync(files, "index.ts");

        bundle.IsFailure.Should().BeTrue();
        bundle.ErrorCode.Should().Be("SCRIPT_BUILD_FAILED");
        List<ScriptBuildError> errors = bundle
            .ErrorData.Should()
            .BeAssignableTo<IEnumerable<ScriptBuildError>>()
            .Subject.ToList();
        errors.Should().ContainSingle();
        errors[0].Position.Should().Be(new ScriptSourcePosition("index.ts", 1, 10));
    }

    [Fact]
    public async Task A_package_import_fails_because_scripts_have_no_npm()
    {
        Dictionary<string, string> files = new()
        {
            ["index.ts"] = "import _ from 'lodash';\nbot.send(_.VERSION);\n",
        };

        Result<string> bundle = await ScriptBundlers.Real().BundleAsync(files, "index.ts");

        bundle.IsFailure.Should().BeTrue();
        bundle.ErrorCode.Should().Be("SCRIPT_BUILD_FAILED");
        bundle.ErrorMessage.Should().StartWith("index.ts:1:15: Could not resolve \"lodash\"");
    }

    [Fact]
    public async Task Every_error_is_reported_not_only_the_first()
    {
        Dictionary<string, string> files = new()
        {
            ["index.ts"] = "import a from './a';\nimport b from './b';\nbot.send(a + b);\n",
        };

        Result<string> bundle = await ScriptBundlers.Real().BundleAsync(files, "index.ts");

        bundle
            .ErrorMessage.Should()
            .Be(
                "index.ts:1:15: Could not resolve \"./a\"\n"
                    + "index.ts:2:15: Could not resolve \"./b\""
            );
    }

    [Fact]
    public async Task A_path_that_escapes_the_project_is_refused_before_anything_runs()
    {
        IProcessRunner runner = Substitute.For<IProcessRunner>();
        Dictionary<string, string> files = new()
        {
            ["index.ts"] = "import x from '../evil';\n",
            ["../evil.ts"] = "export default 1;\n",
        };

        Result<string> bundle = await ScriptBundlers.With(runner).BundleAsync(files, "index.ts");

        bundle.IsFailure.Should().BeTrue();
        bundle.ErrorCode.Should().Be("SCRIPT_PROJECT_PATH_INVALID");
        bundle.ErrorMessage.Should().Contain("../evil.ts");
        await runner.DidNotReceiveWithAnyArgs().RunAsync(default!);
    }

    [Fact]
    public async Task A_missing_esbuild_says_how_to_install_it()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Widgets:EsbuildPath"] = "nnz-esbuild-that-does-not-exist",
                }
            )
            .Build();
        EsbuildScriptBundler sut = new(
            new ProcessRunner(),
            configuration,
            NullLogger<EsbuildScriptBundler>.Instance
        );

        Result<string> bundle = await sut.BundleAsync(
            new Dictionary<string, string> { ["index.ts"] = "bot.send('x');\n" },
            "index.ts"
        );

        bundle.IsFailure.Should().BeTrue();
        bundle.ErrorCode.Should().Be("SCRIPT_BUILD_TOOL_UNAVAILABLE");
        bundle.ErrorMessage.Should().Contain("nnz-esbuild-that-does-not-exist");
    }
}
