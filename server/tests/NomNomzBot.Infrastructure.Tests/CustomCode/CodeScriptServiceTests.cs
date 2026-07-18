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
using NomNomzBot.Domain.CustomCode.Events;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.CustomCode.Jint;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Widgets.Bundling;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// Proves the custom-code authoring service (custom-code.md §3.4): create runs validate-on-save (valid → version 1
/// published; invalid → the rejected version is still persisted for audit and CurrentVersionId stays null);
/// duplicate names are rejected; a new version appends and can hot-swap the active pointer; the list projects the
/// active version's status.
/// </summary>
public sealed class CodeScriptServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000009001");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static (CodeScriptService Sut, AuthDbContext Db, RecordingEventBus Bus) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ICurrentTenantService tenant = Substitute.For<ICurrentTenantService>();
        tenant.BroadcasterId.Returns(Channel);
        RecordingEventBus bus = new();
        CodeScriptService sut = new(
            db,
            tenant,
            new JintScriptExecutor(),
            bus,
            new FakeTimeProvider(Now),
            new WidgetDependencyAllowlist()
        );
        return (sut, db, bus);
    }

    [Fact]
    public async Task Create_with_valid_source_publishes_version_1()
    {
        (CodeScriptService sut, AuthDbContext db, RecordingEventBus bus) = Build();

        Result<CodeScriptDetailDto> r = await sut.CreateAsync(
            new CreateCodeScriptRequest("greet", "desc", "var x = bot.args[0];")
        );

        r.IsSuccess.Should().BeTrue();
        r.Value.CurrentVersion!.Version.Should().Be(1);
        r.Value.CurrentVersion.ValidationStatus.Should().Be("valid");
        db.CodeScripts.Single().CurrentVersionId.Should().NotBeNull();
        bus.Published.OfType<CodeScriptValidatedEvent>()
            .Should()
            .ContainSingle(e => e.ValidationStatus == "valid");
    }

    [Fact]
    public async Task Create_with_invalid_source_rejects_but_persists_the_version()
    {
        (CodeScriptService sut, AuthDbContext db, _) = Build();

        Result<CodeScriptDetailDto> r = await sut.CreateAsync(
            new CreateCodeScriptRequest("bad", null, "var x = (((;")
        );

        r.ErrorCode.Should().Be("VALIDATION_FAILED");
        db.CodeScriptVersions.Single().ValidationStatus.Should().Be("rejected");
        db.CodeScripts.Single().CurrentVersionId.Should().BeNull();
    }

    [Fact]
    public async Task Create_with_a_duplicate_name_is_rejected()
    {
        (CodeScriptService sut, _, _) = Build();
        await sut.CreateAsync(new CreateCodeScriptRequest("dup", null, "var x = 1;"));

        Result<CodeScriptDetailDto> r = await sut.CreateAsync(
            new CreateCodeScriptRequest("dup", null, "var y = 2;")
        );

        r.ErrorCode.Should().Be("ALREADY_EXISTS");
    }

    [Fact]
    public async Task CreateVersion_appends_and_hot_swaps_when_published()
    {
        (CodeScriptService sut, AuthDbContext db, _) = Build();
        Guid id = (await sut.CreateAsync(new CreateCodeScriptRequest("s", null, "var x = 1;")))
            .Value
            .Id;

        Result<CodeScriptVersionDto> r = await sut.CreateVersionAsync(
            id,
            new CreateCodeScriptVersionRequest("var y = 2;", Publish: true)
        );

        r.Value.Version.Should().Be(2);
        Guid v2 = db.CodeScriptVersions.Single(v => v.Version == 2).Id;
        db.CodeScripts.Single().CurrentVersionId.Should().Be(v2);
    }

    [Fact]
    public async Task List_projects_the_active_version_status()
    {
        (CodeScriptService sut, _, _) = Build();
        await sut.CreateAsync(new CreateCodeScriptRequest("a", null, "var x = 1;"));

        PagedList<CodeScriptSummaryDto> page = (await sut.ListAsync(new PaginationParams())).Value;

        page.TotalCount.Should().Be(1);
        page.Items[0].CurrentValidationStatus.Should().Be("valid");
        page.Items[0].CurrentVersion.Should().Be(1);
    }
}
