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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.DevPlatform.Dtos;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.CustomCode.Jint;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Widgets.Bundling;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// A plan without script time (Free: <c>sandbox_exec_ms</c> 0) cannot write scripts. Creating one, saving a
/// version, publishing a version and turning one on each return the plan refusal and store nothing new. Turning a
/// script off and deleting it still work, so a channel that drops to that plan can clean up. On a plan with script
/// time every write works as before.
/// </summary>
public sealed class CodeScriptServicePlanGateTests
{
    private const string Refusal = "Code scripts need the Base plan.";

    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000092a1");
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private static (CodeScriptService Sut, AuthDbContext Db, ScriptBudget Budget) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ICurrentTenantService tenant = Substitute.For<ICurrentTenantService>();
        tenant.BroadcasterId.Returns(Channel);
        ScriptBudget budget = new();
        CodeScriptService sut = new(
            db,
            tenant,
            new JintScriptExecutor(),
            ScriptBundlers.Real(),
            new RecordingEventBus(),
            new FakeTimeProvider(Now),
            new WidgetDependencyAllowlist(),
            budget
        );
        return (sut, db, budget);
    }

    private static ProjectDto Project(string source) =>
        new(
            new Dictionary<string, string> { ["index.ts"] = source },
            new("index.ts", "script", "typescript", [])
        );

    [Fact]
    public async Task Create_on_a_plan_without_script_time_is_refused_and_stores_nothing()
    {
        (CodeScriptService sut, AuthDbContext db, ScriptBudget budget) = Build();
        budget.LimitMs = 0;

        Result<CodeScriptDetailDto> r = await sut.CreateAsync(new("s", null, "var x = 1;"));

        r.IsFailure.Should().BeTrue();
        r.ErrorCode.Should().Be("PLAN_REQUIRED");
        r.ErrorMessage.Should().Be(Refusal);
        db.CodeScripts.Count().Should().Be(0);
        db.CodeScriptVersions.Count().Should().Be(0);
    }

    [Fact]
    public async Task Create_project_on_a_plan_without_script_time_is_refused_and_stores_nothing()
    {
        (CodeScriptService sut, AuthDbContext db, ScriptBudget budget) = Build();
        budget.LimitMs = 0;

        Result<CodeScriptDetailDto> r = await sut.CreateProjectAsync(
            "s",
            null,
            Project("var x = 1;")
        );

        r.ErrorCode.Should().Be("PLAN_REQUIRED");
        r.ErrorMessage.Should().Be(Refusal);
        db.CodeScripts.Count().Should().Be(0);
        db.CodeScriptVersions.Count().Should().Be(0);
    }

    [Fact]
    public async Task Save_version_on_a_plan_without_script_time_is_refused_and_keeps_the_live_version()
    {
        (CodeScriptService sut, AuthDbContext db, ScriptBudget budget) = Build();
        Guid id = (await sut.CreateAsync(new("s", null, "var x = 1;"))).Value.Id;
        Guid live = db.CodeScripts.Single().CurrentVersionId!.Value;
        budget.LimitMs = 0;

        Result<CodeScriptVersionDto> r = await sut.CreateVersionAsync(
            id,
            new("var y = 2;", Publish: true)
        );

        r.ErrorCode.Should().Be("PLAN_REQUIRED");
        r.ErrorMessage.Should().Be(Refusal);
        db.CodeScriptVersions.Count().Should().Be(1);
        db.CodeScripts.Single().CurrentVersionId.Should().Be(live);
    }

    [Fact]
    public async Task Save_project_on_a_plan_without_script_time_is_refused_and_keeps_the_live_version()
    {
        (CodeScriptService sut, AuthDbContext db, ScriptBudget budget) = Build();
        Guid id = (await sut.CreateAsync(new("s", null, "var x = 1;"))).Value.Id;
        Guid live = db.CodeScripts.Single().CurrentVersionId!.Value;
        budget.LimitMs = 0;

        Result<CodeScriptVersionDto> r = await sut.SaveProjectAsync(id, Project("var y = 2;"));

        r.ErrorCode.Should().Be("PLAN_REQUIRED");
        r.ErrorMessage.Should().Be(Refusal);
        db.CodeScriptVersions.Count().Should().Be(1);
        db.CodeScripts.Single().CurrentVersionId.Should().Be(live);
    }

    [Fact]
    public async Task Publish_on_a_plan_without_script_time_is_refused_and_keeps_the_live_version()
    {
        (CodeScriptService sut, AuthDbContext db, ScriptBudget budget) = Build();
        Guid id = (await sut.CreateAsync(new("s", null, "var x = 1;"))).Value.Id;
        Guid live = db.CodeScripts.Single().CurrentVersionId!.Value;
        Guid draft = (await sut.CreateVersionAsync(id, new("var y = 2;", Publish: false))).Value.Id;
        budget.LimitMs = 0;

        Result<CodeScriptDetailDto> r = await sut.PublishVersionAsync(id, draft);

        r.ErrorCode.Should().Be("PLAN_REQUIRED");
        r.ErrorMessage.Should().Be(Refusal);
        db.CodeScripts.Single().CurrentVersionId.Should().Be(live);
    }

    [Fact]
    public async Task Turning_a_script_on_without_script_time_is_refused_and_leaves_it_off()
    {
        (CodeScriptService sut, AuthDbContext db, ScriptBudget budget) = Build();
        Guid id = (await sut.CreateAsync(new("s", null, "var x = 1;"))).Value.Id;
        budget.LimitMs = 0;

        Result r = await sut.SetEnabledAsync(id, isEnabled: true);

        r.ErrorCode.Should().Be("PLAN_REQUIRED");
        r.ErrorMessage.Should().Be(Refusal);
        db.CodeScripts.Single().IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Turning_a_script_off_and_deleting_it_still_work_without_script_time()
    {
        (CodeScriptService sut, AuthDbContext db, ScriptBudget budget) = Build();
        Guid id = (await sut.CreateAsync(new("s", null, "var x = 1;"))).Value.Id;
        (await sut.SetEnabledAsync(id, isEnabled: true)).IsSuccess.Should().BeTrue();
        budget.LimitMs = 0;

        Result off = await sut.SetEnabledAsync(id, isEnabled: false);

        off.IsSuccess.Should().BeTrue(off.ErrorMessage);
        db.CodeScripts.Single().IsEnabled.Should().BeFalse();

        Result deleted = await sut.DeleteAsync(id);

        deleted.IsSuccess.Should().BeTrue(deleted.ErrorMessage);
        db.CodeScripts.Count(s => s.DeletedAt == null).Should().Be(0);
    }

    [Fact]
    public async Task Every_write_works_on_a_plan_with_script_time()
    {
        (CodeScriptService sut, AuthDbContext db, ScriptBudget budget) = Build();
        budget.LimitMs = 600_000;

        Guid id = (await sut.CreateAsync(new("s", null, "var x = 1;"))).Value.Id;
        Result<CodeScriptVersionDto> saved = await sut.CreateVersionAsync(
            id,
            new("var y = 2;", Publish: false)
        );
        Result<CodeScriptDetailDto> published = await sut.PublishVersionAsync(id, saved.Value.Id);
        Result on = await sut.SetEnabledAsync(id, isEnabled: true);

        saved.IsSuccess.Should().BeTrue(saved.ErrorMessage);
        published.IsSuccess.Should().BeTrue(published.ErrorMessage);
        on.IsSuccess.Should().BeTrue(on.ErrorMessage);
        db.CodeScriptVersions.Count().Should().Be(2);
        db.CodeScripts.Single().CurrentVersionId.Should().Be(saved.Value.Id);
        db.CodeScripts.Single().IsEnabled.Should().BeTrue();
    }
}
