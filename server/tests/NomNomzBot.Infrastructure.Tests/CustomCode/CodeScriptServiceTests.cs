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
            new("greet", "desc", "var x = bot.args[0];")
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

        Result<CodeScriptDetailDto> r = await sut.CreateAsync(new("bad", null, "var x = (((;"));

        r.ErrorCode.Should().Be("VALIDATION_FAILED");
        db.CodeScriptVersions.Single().ValidationStatus.Should().Be("rejected");
        db.CodeScripts.Single().CurrentVersionId.Should().BeNull();
    }

    [Fact]
    public async Task Create_with_a_duplicate_name_is_rejected()
    {
        (CodeScriptService sut, _, _) = Build();
        await sut.CreateAsync(new("dup", null, "var x = 1;"));

        Result<CodeScriptDetailDto> r = await sut.CreateAsync(new("dup", null, "var y = 2;"));

        r.ErrorCode.Should().Be("ALREADY_EXISTS");
    }

    [Fact]
    public async Task CreateVersion_appends_and_hot_swaps_when_published()
    {
        (CodeScriptService sut, AuthDbContext db, _) = Build();
        Guid id = (await sut.CreateAsync(new("s", null, "var x = 1;"))).Value.Id;

        Result<CodeScriptVersionDto> r = await sut.CreateVersionAsync(
            id,
            new("var y = 2;", Publish: true)
        );

        r.Value.Version.Should().Be(2);
        Guid v2 = db.CodeScriptVersions.Single(v => v.Version == 2).Id;
        db.CodeScripts.Single().CurrentVersionId.Should().Be(v2);
    }

    [Fact]
    public async Task List_projects_the_active_version_status()
    {
        (CodeScriptService sut, _, _) = Build();
        await sut.CreateAsync(new("a", null, "var x = 1;"));

        PagedList<CodeScriptSummaryDto> page = (await sut.ListAsync(new())).Value;

        page.TotalCount.Should().Be(1);
        page.Items[0].CurrentValidationStatus.Should().Be("valid");
        page.Items[0].CurrentVersion.Should().Be(1);
    }

    // ─── S-OWN06: delete a saved version + real pagination ─────────────────────────

    [Fact]
    public async Task DeleteVersionAsync_removes_a_non_published_version_from_a_later_list_call()
    {
        (CodeScriptService sut, _, _) = Build();
        Guid id = (await sut.CreateAsync(new("s", null, "var x = 1;"))).Value.Id; // v1, published
        Guid v2 = (await sut.CreateVersionAsync(id, new("var x = 2;", Publish: false))).Value.Id; // v2, not published

        Result deleteResult = await sut.DeleteVersionAsync(id, v2);

        deleteResult.IsSuccess.Should().BeTrue();
        PagedList<CodeScriptVersionDto> after = (await sut.ListVersionsAsync(id, new())).Value;
        after.Items.Should().NotContain(v => v.Id == v2);
        after
            .TotalCount.Should()
            .Be(1, "the deleted version must drop out of the count, not just the page");
    }

    [Fact]
    public async Task DeleteVersionAsync_refuses_to_delete_the_currently_published_version()
    {
        (CodeScriptService sut, AuthDbContext db, _) = Build();
        Guid id = (await sut.CreateAsync(new("s", null, "var x = 1;"))).Value.Id;
        Guid publishedVersionId = db.CodeScripts.Single().CurrentVersionId!.Value;

        Result r = await sut.DeleteVersionAsync(id, publishedVersionId);

        r.IsFailure.Should().BeTrue();
        r.ErrorCode.Should().Be("VERSION_IS_PUBLISHED");
        db.CodeScriptVersions.Single(v => v.Id == publishedVersionId)
            .Should()
            .NotBeNull("a refused delete must leave the published version's row untouched");
    }

    [Fact]
    public async Task DeleteVersionAsync_returns_not_found_for_an_unknown_version()
    {
        (CodeScriptService sut, _, _) = Build();
        Guid id = (await sut.CreateAsync(new("s", null, "var x = 1;"))).Value.Id;

        Result r = await sut.DeleteVersionAsync(id, Guid.NewGuid());

        r.IsFailure.Should().BeTrue();
        r.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task ListVersionsAsync_paginates_a_history_larger_than_one_page()
    {
        (CodeScriptService sut, _, _) = Build();
        Guid id = (await sut.CreateAsync(new("s", null, "var x = 1;"))).Value.Id; // v1
        for (int i = 2; i <= 5; i++)
            await sut.CreateVersionAsync(id, new($"var x = {i};", Publish: false)); // v2..v5 — 5 total

        PagedList<CodeScriptVersionDto> page1 = (
            await sut.ListVersionsAsync(id, new(Page: 1, PageSize: 2))
        ).Value;
        PagedList<CodeScriptVersionDto> page2 = (
            await sut.ListVersionsAsync(id, new(Page: 2, PageSize: 2))
        ).Value;
        PagedList<CodeScriptVersionDto> page3 = (
            await sut.ListVersionsAsync(id, new(Page: 3, PageSize: 2))
        ).Value;

        page1.TotalCount.Should().Be(5);
        // Newest first: v5, v4 | v3, v2 | v1 — exercises the real Skip/Take slice, not just a total count.
        page1.Items.Select(v => v.Version).Should().Equal(5, 4);
        page2.Items.Select(v => v.Version).Should().Equal(3, 2);
        page3.Items.Select(v => v.Version).Should().Equal(1);
    }
}
