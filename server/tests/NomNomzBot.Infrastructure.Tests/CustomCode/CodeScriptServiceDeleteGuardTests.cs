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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.CustomCode.Entities;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.CustomCode.Jint;
using NomNomzBot.Infrastructure.Tests.Commands;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Widgets.Bundling;
using NSubstitute;
using CommandEntity = NomNomzBot.Domain.Commands.Entities.Command;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// A code script that a command runs directly cannot be deleted: the command would fail at run time with
/// "Script not found". The refusal names the commands that use it and leaves the script in place.
/// </summary>
public sealed class CodeScriptServiceDeleteGuardTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f501");

    private static (CodeScriptService Sut, CommandsTestDbContext Db, Guid ScriptId) Build()
    {
        CommandsTestDbContext db = CommandsTestDbContext.New();
        CodeScript script = new() { BroadcasterId = Channel, Name = "lucky" };
        db.CodeScripts.Add(script);
        db.SaveChanges();

        ICurrentTenantService tenant = Substitute.For<ICurrentTenantService>();
        tenant.BroadcasterId.Returns(Channel);
        CodeScriptService sut = new(
            db,
            tenant,
            new JintScriptExecutor(),
            ScriptBundlers.Real(),
            new RecordingEventBus(),
            TimeProvider.System,
            new WidgetDependencyAllowlist()
        );
        return (sut, db, script.Id);
    }

    [Fact]
    public async Task A_script_a_command_runs_is_not_deleted_and_the_refusal_names_the_command()
    {
        (CodeScriptService sut, CommandsTestDbContext db, Guid scriptId) = Build();
        db.Commands.Add(
            new CommandEntity
            {
                BroadcasterId = Channel,
                Name = "luckyfeather",
                NameNormalized = "luckyfeather",
                Tier = "code",
                CodeScriptId = scriptId,
            }
        );
        await db.SaveChangesAsync();

        Result deleted = await sut.DeleteAsync(scriptId);

        deleted.IsFailure.Should().BeTrue();
        deleted.ErrorCode.Should().Be("CODE_SCRIPT_IN_USE");
        deleted.ErrorMessage.Should().Contain("luckyfeather");
        (await db.CodeScripts.IgnoreQueryFilters().AnyAsync(s => s.Id == scriptId))
            .Should()
            .BeTrue("a refused delete leaves the script alone");
    }

    [Fact]
    public async Task A_script_no_command_runs_is_deleted()
    {
        (CodeScriptService sut, CommandsTestDbContext db, Guid scriptId) = Build();

        Result deleted = await sut.DeleteAsync(scriptId);

        deleted.IsSuccess.Should().BeTrue();
        // This test context has no soft-delete interceptor, so the removal is a real delete here.
        (await db.CodeScripts.IgnoreQueryFilters().AnyAsync(s => s.Id == scriptId))
            .Should()
            .BeFalse("the delete went through");
    }
}
