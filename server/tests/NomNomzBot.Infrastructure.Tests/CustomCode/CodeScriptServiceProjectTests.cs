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
using NomNomzBot.Application.DevPlatform.Dtos;
using NomNomzBot.Application.DevPlatform.Projects;
using NomNomzBot.Domain.CustomCode.Entities;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.CustomCode.Jint;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Widgets.Bundling;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// Behavior tests for the code-script multi-file project CRUD (dev-platform.md §8). Proves the consequences: a saved
/// project appends a validated version whose stored file set + manifest round-trip through GET; a rejected compile,
/// a missing manifest entry, or an un-allowlisted dependency each fail with the reason and persist NO version; and
/// read/write are scoped to the owning tenant. Runs the REAL <see cref="JintScriptExecutor"/> so validate-on-save is
/// genuine, over the shared auth in-memory harness the other custom-code tests use.
/// </summary>
public sealed class CodeScriptServiceProjectTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000090a1");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-0000000090b2");
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    private static CodeScriptService ServiceFor(AuthDbContext db, Guid tenant)
    {
        ICurrentTenantService tenantService = Substitute.For<ICurrentTenantService>();
        tenantService.BroadcasterId.Returns(tenant);
        return new CodeScriptService(
            db,
            tenantService,
            new JintScriptExecutor(),
            new RecordingEventBus(),
            new FakeTimeProvider(Now),
            new WidgetDependencyAllowlist()
        );
    }

    private static ProjectDto Project(
        Dictionary<string, string> files,
        string entry,
        params string[] deps
    ) => new(files, new ProjectManifestDto(entry, "script", "typescript", deps));

    private static async Task<Guid> SeedScriptAsync(CodeScriptService sut)
    {
        Result<CodeScriptDetailDto> created = await sut.CreateAsync(
            new CreateCodeScriptRequest("greeter", "desc", "var x = bot.args[0];")
        );
        created.IsSuccess.Should().BeTrue(created.ErrorMessage);
        return created.Value.Id;
    }

    [Fact]
    public async Task SaveProject_ValidMultiFile_AppendsAndPublishes_AndRoundTripsViaGet()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        CodeScriptService sut = ServiceFor(db, Channel);
        Guid scriptId = await SeedScriptAsync(sut);

        Dictionary<string, string> files = new()
        {
            ["index.ts"] = "var greeting = bot.args[0];",
            ["lib/util.ts"] = "export const util = 1;",
        };

        Result<CodeScriptVersionDto> saved = await sut.SaveProjectAsync(
            scriptId,
            Project(files, "index.ts")
        );

        saved.IsSuccess.Should().BeTrue(saved.ErrorMessage);
        saved.Value.Version.Should().Be(2); // appended after the create's v1
        saved.Value.ValidationStatus.Should().Be("valid");

        // The whole project is stored on the new version, and it is now the live (current) version.
        CodeScriptVersion stored = db.CodeScriptVersions.Single(v =>
            v.CodeScriptId == scriptId && v.Version == 2
        );
        ProjectManifest? manifest = ProjectJson.DeserializeManifest(stored.ManifestJson);
        manifest!.Entry.Should().Be("index.ts");
        ProjectJson.DeserializeFiles(stored.FilesJson).Should().BeEquivalentTo(files);
        db.CodeScripts.Single(s => s.Id == scriptId).CurrentVersionId.Should().Be(stored.Id);

        // GET returns the exact file set + manifest that were saved.
        Result<ProjectDto> got = await sut.GetProjectAsync(scriptId);
        got.IsSuccess.Should().BeTrue(got.ErrorMessage);
        got.Value.Files.Should().BeEquivalentTo(files);
        got.Value.Manifest.Entry.Should().Be("index.ts");
        got.Value.Manifest.Kind.Should().Be("script");
        got.Value.Manifest.Framework.Should().Be("typescript");
    }

    [Fact]
    public async Task GetProject_ForASingleFileScript_ProjectsTheOneFileScaffold()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        CodeScriptService sut = ServiceFor(db, Channel);
        Guid scriptId = await SeedScriptAsync(sut);

        Result<ProjectDto> got = await sut.GetProjectAsync(scriptId);

        got.IsSuccess.Should().BeTrue(got.ErrorMessage);
        got.Value.Files.Should().ContainKey("index.ts");
        got.Value.Files["index.ts"].Should().Be("var x = bot.args[0];");
        got.Value.Manifest.Entry.Should().Be("index.ts");
        got.Value.Manifest.Kind.Should().Be("script");
    }

    [Fact]
    public async Task SaveProject_RejectedCompile_FailsWithReason_PersistsNoVersion()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        CodeScriptService sut = ServiceFor(db, Channel);
        Guid scriptId = await SeedScriptAsync(sut);

        Dictionary<string, string> files = new() { ["index.ts"] = "var x = (((;" };

        Result<CodeScriptVersionDto> saved = await sut.SaveProjectAsync(
            scriptId,
            Project(files, "index.ts")
        );

        saved.IsFailure.Should().BeTrue();
        saved.ErrorCode.Should().Be("VALIDATION_FAILED");
        // Only the create's v1 exists — the rejected project save persisted nothing.
        db.CodeScriptVersions.Count(v => v.CodeScriptId == scriptId).Should().Be(1);
    }

    [Fact]
    public async Task SaveProject_ManifestEntryMissing_FailsValidation_PersistsNoVersion()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        CodeScriptService sut = ServiceFor(db, Channel);
        Guid scriptId = await SeedScriptAsync(sut);

        Dictionary<string, string> files = new() { ["index.ts"] = "var x = 1;" };

        Result<CodeScriptVersionDto> saved = await sut.SaveProjectAsync(
            scriptId,
            Project(files, "main.ts") // entry not present
        );

        saved.IsFailure.Should().BeTrue();
        saved.ErrorCode.Should().Be("VALIDATION_FAILED");
        saved.ErrorMessage.Should().Contain("main.ts");
        db.CodeScriptVersions.Count(v => v.CodeScriptId == scriptId).Should().Be(1);
    }

    [Fact]
    public async Task SaveProject_NonAllowlistedDependency_FailsValidation_PersistsNoVersion()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        CodeScriptService sut = ServiceFor(db, Channel);
        Guid scriptId = await SeedScriptAsync(sut);

        Dictionary<string, string> files = new() { ["index.ts"] = "var x = 1;" };

        Result<CodeScriptVersionDto> saved = await sut.SaveProjectAsync(
            scriptId,
            Project(files, "index.ts", "lodash")
        );

        saved.IsFailure.Should().BeTrue();
        saved.ErrorCode.Should().Be("VALIDATION_FAILED");
        saved.ErrorMessage.Should().Contain("lodash");
        db.CodeScriptVersions.Count(v => v.CodeScriptId == scriptId).Should().Be(1);
    }

    [Fact]
    public async Task Project_ReadAndWrite_AreScopedToTheOwningTenant()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        CodeScriptService owner = ServiceFor(db, Channel);
        Guid scriptId = await SeedScriptAsync(owner);

        // A different tenant, over the SAME store, sees neither read nor write.
        CodeScriptService intruder = ServiceFor(db, OtherChannel);
        Dictionary<string, string> files = new() { ["index.ts"] = "var x = 1;" };

        Result<CodeScriptVersionDto> write = await intruder.SaveProjectAsync(
            scriptId,
            Project(files, "index.ts")
        );
        write.IsFailure.Should().BeTrue();
        write.ErrorCode.Should().Be("NOT_FOUND");

        Result<ProjectDto> read = await intruder.GetProjectAsync(scriptId);
        read.IsFailure.Should().BeTrue();
        read.ErrorCode.Should().Be("NOT_FOUND");

        // The owner still has exactly its create's v1 — the intruder wrote nothing.
        db.CodeScriptVersions.Count(v => v.CodeScriptId == scriptId).Should().Be(1);
    }
}
