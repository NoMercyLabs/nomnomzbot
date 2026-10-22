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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.CustomCode.Enums;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.AutomationApi;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands;

/// <summary>
/// A command with tier "code" runs its script straight through <see cref="IScriptRunner"/>: the runner gets the
/// script id, the words after the command as args and the chat variables, and neither the pipeline engine nor a
/// plain chat message is involved.
/// </summary>
public sealed class CommandInvokerCodeTierTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f401");
    private static readonly Guid Script = Guid.Parse("0192a000-0000-7000-8000-00000000f402");

    private sealed record Rig(
        CommandInvoker Invoker,
        IScriptRunner Runner,
        IPipelineEngine Engine,
        IChatProvider Chat,
        Guid CommandId
    );

    private static Rig Build(ScriptExecutionOutcome outcome, string? error = null)
    {
        AutomationTestDbContext db = AutomationTestDbContext.New();
        Guid commandId = Guid.NewGuid();
        db.Commands.Add(
            new Command
            {
                Id = commandId,
                BroadcasterId = Channel,
                Name = "lucky",
                NameNormalized = "lucky",
                Tier = "code",
                CodeScriptId = Script,
            }
        );
        db.SaveChanges();

        IScriptRunner runner = Substitute.For<IScriptRunner>();
        runner
            .RunAsync(Arg.Any<Guid>(), Arg.Any<ScriptInvocation>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new ScriptRunResult(
                        outcome,
                        new Dictionary<string, string>(),
                        "script output",
                        false,
                        error,
                        null
                    )
                )
            );
        IPipelineEngine engine = Substitute.For<IPipelineEngine>();
        IChatProvider chat = Substitute.For<IChatProvider>();
        IBuiltinCommandCatalog catalog = Substitute.For<IBuiltinCommandCatalog>();

        CommandInvoker invoker = new(
            db,
            engine,
            Substitute.For<IOwnerActionService>(),
            Substitute.For<IChannelTwitchBotResolver>(),
            Substitute.For<ITemplateResolver>(),
            chat,
            catalog,
            new OutboundSanctionAccessor(),
            runner
        );
        return new Rig(invoker, runner, engine, chat, commandId);
    }

    private static Task<Result<InvocationOutcome>> Run(Rig rig) =>
        rig.Invoker.InvokeAsync(
            Channel,
            new BindingTarget(BindingTargetKind.Command, rig.CommandId.ToString()),
            new InvocationContext("u-1", "Viewer One", "@kitte hello", "chat", "vip")
        );

    [Fact]
    public async Task A_code_command_runs_its_script_with_the_chat_args_and_variables_and_sends_nothing_else()
    {
        Rig rig = Build(ScriptExecutionOutcome.Success);

        Result<InvocationOutcome> result = await Run(rig);

        result.IsSuccess.Should().BeTrue();
        result.Value.Succeeded.Should().BeTrue();
        ScriptInvocation invocation = (ScriptInvocation)
            rig.Runner.ReceivedCalls().Single().GetArguments()[1]!;
        rig.Runner.ReceivedCalls().Single().GetArguments()[0].Should().Be(Script);
        invocation.Args.Should().Equal("@kitte", "hello");
        invocation.TriggeredByUserId.Should().Be("u-1");
        invocation.TriggeredByDisplayName.Should().Be("Viewer One");
        invocation.Variables["args"].Should().Be("@kitte hello");
        invocation.Variables["user"].Should().Be("Viewer One");
        invocation.ReplyTo.Should().BeNull("a run outside chat has no message to thread under");
        rig.Engine.ReceivedCalls().Should().BeEmpty();
        rig.Chat.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task A_script_that_does_not_complete_is_reported_as_a_failed_outcome_with_its_error()
    {
        Rig rig = Build(ScriptExecutionOutcome.Faulted, "boom in the script");

        Result<InvocationOutcome> result = await Run(rig);

        result.IsSuccess.Should().BeTrue();
        result.Value.Succeeded.Should().BeFalse();
        result.Value.Error.Should().Be("boom in the script");
    }
}
