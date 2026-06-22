// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.CustomCode.Entities;
using NomNomzBot.Domain.CustomCode.Enums;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.CustomCode.Jint;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// Proves the script run orchestrator (custom-code.md §3.5): a run executes the active valid version and surfaces
/// its output, recording LastRanAt; a disabled script or one with no valid published version fails closed (Faulted)
/// before reaching the executor.
/// </summary>
public sealed class ScriptRunnerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000a001");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static (ScriptRunner Sut, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IScriptCapabilityBroker broker = Substitute.For<IScriptCapabilityBroker>();
        broker
            .BuildGrantAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => Result.Success(new ScriptCapabilityGrant(Channel, [])));
        IScriptExecutionMeter meter = Substitute.For<IScriptExecutionMeter>();
        meter
            .CheckSandboxBudgetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new QuotaCheck(true, -1, 0, default, default)));
        meter
            .RecordSandboxUsageAsync(
                Arg.Any<Guid>(),
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        return (
            new ScriptRunner(
                db,
                new JintScriptExecutor(),
                broker,
                meter,
                Substitute.For<NomNomzBot.Application.Abstractions.Transport.ITwitchChatService>(),
                Substitute.For<NomNomzBot.Application.Abstractions.Transport.ITwitchIdentityResolver>(),
                new FakeTimeProvider(Now)
            ),
            db
        );
    }

    private static async Task<Guid> SeedAsync(
        AuthDbContext db,
        bool enabled,
        bool withValidVersion,
        string js = "bot.send('hello');"
    )
    {
        CodeScript script = new()
        {
            BroadcasterId = Channel,
            Name = "s",
            Language = "typescript",
            IsEnabled = enabled,
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime,
        };
        db.CodeScripts.Add(script);
        if (withValidVersion)
        {
            CodeScriptVersion version = new()
            {
                CodeScriptId = script.Id,
                BroadcasterId = Channel,
                Version = 1,
                SourceCode = js,
                CompiledJs = js,
                CompiledHash = "h",
                ValidationStatus = "valid",
                CreatedAt = Now.UtcDateTime,
            };
            db.CodeScriptVersions.Add(version);
            script.CurrentVersionId = version.Id;
        }
        await db.SaveChangesAsync();
        return script.Id;
    }

    private static ScriptInvocation Invocation() =>
        new("exec-1", "u1", "User", [], new Dictionary<string, string>());

    [Fact]
    public async Task Runs_the_active_version_and_records_last_ran()
    {
        (ScriptRunner sut, AuthDbContext db) = Build();
        Guid id = await SeedAsync(db, enabled: true, withValidVersion: true);

        ScriptRunResult r = (await sut.RunAsync(id, Invocation())).Value;

        r.Outcome.Should().Be(ScriptExecutionOutcome.Success);
        r.Output.Should().Be("hello");
        db.CodeScripts.Single().LastRanAt.Should().NotBeNull();
    }

    [Fact]
    public async Task A_disabled_script_fails_closed()
    {
        (ScriptRunner sut, AuthDbContext db) = Build();
        Guid id = await SeedAsync(db, enabled: false, withValidVersion: true);

        ScriptRunResult r = (await sut.RunAsync(id, Invocation())).Value;

        r.Outcome.Should().Be(ScriptExecutionOutcome.Faulted);
    }

    [Fact]
    public async Task A_script_with_no_valid_version_fails_closed()
    {
        (ScriptRunner sut, AuthDbContext db) = Build();
        Guid id = await SeedAsync(db, enabled: true, withValidVersion: false);

        ScriptRunResult r = (await sut.RunAsync(id, Invocation())).Value;

        r.Outcome.Should().Be(ScriptExecutionOutcome.Faulted);
    }
}
