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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.CustomCode.Enums;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.CustomCode.Jint;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Widgets.Bundling;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// S-OWN05 regression: an author extracting an array to its own file and doing
/// <c>import SCENES from './scenes';</c> in the entry must actually work end to end, not just save without a syntax
/// error. Proves the full real path — <see cref="CodeScriptService.SaveProjectAsync"/> (which resolves the project's
/// imports via <see cref="ScriptImportResolver"/> before validate-on-save) through <see cref="ScriptRunner"/> into a
/// REAL <see cref="JintScriptExecutor"/> — by asserting on the actual runtime value the imported module produced,
/// not merely "it didn't throw".
/// </summary>
public sealed class ScriptImportResolutionTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000091a1");
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static CodeScriptService ServiceFor(AuthDbContext db)
    {
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        tenantService.BroadcasterId.Returns(Channel);
        return new(
            db,
            tenantService,
            new JintScriptExecutor(),
            new RecordingEventBus(),
            new FakeTimeProvider(Now),
            new WidgetDependencyAllowlist()
        );
    }

    private static async Task<Guid> SeedScriptAsync(CodeScriptService sut)
    {
        Result<CodeScriptDetailDto> created = await sut.CreateAsync(
            new("scene-picker", "desc", "var x = 1;")
        );
        created.IsSuccess.Should().BeTrue(created.ErrorMessage);
        return created.Value.Id;
    }

    [Fact]
    public async Task DefaultImport_FromASiblingFile_SavesAndExecutesUsingTheImportedArray()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        CodeScriptService codeScriptService = ServiceFor(db);
        Guid scriptId = await SeedScriptAsync(codeScriptService);

        // Exactly the owner's reported shape: an array extracted to its own file, imported by default from the entry.
        Dictionary<string, string> files = new()
        {
            ["index.ts"] =
                "import SCENES from './scenes';\n"
                + "bot.setVar('sceneCount', String(SCENES.length));\n"
                + "bot.send(SCENES[1]);\n",
            ["scenes.ts"] =
                "const SCENES = ['intro', 'gameplay', 'outro'];\nexport default SCENES;\n",
        };

        Result<CodeScriptVersionDto> saved = await codeScriptService.SaveProjectAsync(
            scriptId,
            new(files, new("index.ts", "script", "typescript", []))
        );

        saved.IsSuccess.Should().BeTrue(saved.ErrorMessage);
        saved.Value.ValidationStatus.Should().Be("valid");
        // The stored/displayed source is the entry's OWN unbundled text — the import line stays intact for the editor.
        saved.Value.SourceCode.Should().Contain("import SCENES from './scenes';");

        // Real end-to-end execution: ScriptRunner -> real Jint -> assert on the actual value the import produced.
        ScriptRunner runner = new(
            db,
            new JintScriptExecutor(),
            new AllowAllCapabilityBroker(),
            new NoopScriptExecutionMeter(),
            new StubHostBridgeFactory(),
            new FakeTimeProvider(Now)
        );

        Result<ScriptRunResult> run = await runner.RunAsync(
            scriptId,
            new ScriptInvocation("exec-1", "u1", "Viewer", [], new Dictionary<string, string>())
        );

        run.IsSuccess.Should().BeTrue(run.ErrorMessage);
        run.Value.Outcome.Should().Be(ScriptExecutionOutcome.Success);
        run.Value.VariablesOut["sceneCount"].Should().Be("3");
        run.Value.Output.Should().Be("gameplay"); // SCENES[1] from the imported sibling file
    }

    [Fact]
    public async Task NamedAndAliasedImports_FromASiblingFile_ResolveToTheirLocalBindings()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        CodeScriptService codeScriptService = ServiceFor(db);
        Guid scriptId = await SeedScriptAsync(codeScriptService);

        Dictionary<string, string> files = new()
        {
            ["index.ts"] =
                "import { GREETING, FAREWELL as BYE } from './phrases';\n"
                + "bot.send(GREETING + '/' + BYE);\n",
            ["phrases.ts"] = "export const GREETING = 'hi';\nexport const FAREWELL = 'bye';\n",
        };

        Result<CodeScriptVersionDto> saved = await codeScriptService.SaveProjectAsync(
            scriptId,
            new(files, new("index.ts", "script", "typescript", []))
        );
        saved.IsSuccess.Should().BeTrue(saved.ErrorMessage);
        saved.Value.ValidationStatus.Should().Be("valid");

        ScriptRunner runner = new(
            db,
            new JintScriptExecutor(),
            new AllowAllCapabilityBroker(),
            new NoopScriptExecutionMeter(),
            new StubHostBridgeFactory(),
            new FakeTimeProvider(Now)
        );

        Result<ScriptRunResult> run = await runner.RunAsync(
            scriptId,
            new ScriptInvocation("exec-2", "u1", "Viewer", [], new Dictionary<string, string>())
        );

        run.IsSuccess.Should().BeTrue(run.ErrorMessage);
        run.Value.Outcome.Should().Be(ScriptExecutionOutcome.Success);
        run.Value.Output.Should().Be("hi/bye");
    }

    [Fact]
    public async Task ImportOfAFileNotInTheProject_FailsValidation_WithAnActionableMessage_PersistsNoVersion()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        CodeScriptService codeScriptService = ServiceFor(db);
        Guid scriptId = await SeedScriptAsync(codeScriptService);

        Dictionary<string, string> files = new()
        {
            ["index.ts"] = "import SCENES from './scenes';\nbot.send(String(SCENES.length));\n",
        };

        Result<CodeScriptVersionDto> saved = await codeScriptService.SaveProjectAsync(
            scriptId,
            new(files, new("index.ts", "script", "typescript", []))
        );

        saved.IsFailure.Should().BeTrue();
        saved.ErrorCode.Should().Be("VALIDATION_FAILED");
        saved.ErrorMessage.Should().Contain("./scenes");
        saved.ErrorMessage.Should().Contain("index.ts");
        db.CodeScriptVersions.Count(v => v.CodeScriptId == scriptId).Should().Be(1); // only the seed's v1
    }

    // Minimal real-execution collaborators (ScriptRunner needs concrete implementations of these, not the
    // service-under-test) — deliberately permissive/no-op so the test's only variable is import resolution.
    private sealed class AllowAllCapabilityBroker : IScriptCapabilityBroker
    {
        public IReadOnlyList<ScriptCapabilityDescriptor> Catalog => [];

        public Task<Result<ScriptCapabilityGrant>> BuildGrantAsync(
            Guid broadcasterId,
            IReadOnlyList<string> declaredCapabilities,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success(new ScriptCapabilityGrant(Guid.NewGuid(), [])));
    }

    private sealed class NoopScriptExecutionMeter : IScriptExecutionMeter
    {
        public Task<Result<QuotaCheck>> CheckSandboxBudgetAsync(
            Guid broadcasterId,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                Result.Success(new QuotaCheck(true, -1, 0, DateTime.UnixEpoch, DateTime.UnixEpoch))
            );

        public Task<Result> RecordSandboxUsageAsync(
            Guid broadcasterId,
            long elapsedMs,
            string executionId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success());
    }

    private sealed class StubHostBridgeFactory : IScriptHostBridgeFactory
    {
        public IScriptHostBridge Create(Guid broadcasterId, string triggeringUserId) =>
            new StubBridge();
    }

    private sealed class StubBridge : IScriptHostBridge
    {
        public HostImportDelegate Resolve(string capabilityKey) => (_, _, _) => null;
    }
}
