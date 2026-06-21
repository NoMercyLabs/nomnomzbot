// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Infrastructure.Economy.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Economy;

/// <summary>
/// Proves the economy pipeline actions (economy.md §6): grant/deduct post the correctly-signed ledger entry
/// with the pipeline entry type and publish the new balance into <c>{{balance}}</c>; deduct stops the pipeline
/// on insufficient funds; check_balance writes its variable and gates on <c>min</c>; and jar_contribute routes
/// the viewer's contribution to the jar service.
/// </summary>
public sealed class EconomyPipelineActionsTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000c1");
    private static readonly Guid Viewer = Guid.Parse("0192a000-0000-7000-8000-0000000000c2");
    private static readonly Guid Jar = Guid.Parse("0192a000-0000-7000-8000-0000000000c3");

    private static PipelineExecutionContext Context(string? triggeredBy = null) =>
        new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = triggeredBy ?? Viewer.ToString(),
            TriggeredByDisplayName = "viewer",
            MessageId = "m1",
            RawMessage = "!cmd",
            CancellationToken = default,
        };

    private static ActionDefinition Action(params (string Key, object Value)[] p) =>
        new()
        {
            Type = "x",
            Parameters = p.ToDictionary(
                x => x.Key,
                x => JsonSerializer.SerializeToElement(x.Value)
            ),
        };

    private static CurrencyLedgerEntryDto EntryWithBalance(long balance) =>
        new(
            1,
            1,
            Guid.NewGuid(),
            Viewer,
            0,
            balance,
            "EarnPipeline",
            null,
            null,
            null,
            null,
            null,
            null,
            default
        );

    [Fact]
    public async Task GrantCurrency_credits_and_publishes_the_new_balance()
    {
        ICurrencyAccountService accounts = Substitute.For<ICurrencyAccountService>();
        accounts
            .PostLedgerEntryAsync(
                Channel,
                Arg.Any<PostLedgerEntryCommand>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(EntryWithBalance(150)));
        GrantCurrencyAction sut = new(accounts);
        PipelineExecutionContext ctx = Context();

        ActionResult result = await sut.ExecuteAsync(ctx, Action(("amount", 50)));

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        ctx.Variables["balance"].Should().Be("150");
        await accounts
            .Received(1)
            .PostLedgerEntryAsync(
                Channel,
                Arg.Is<PostLedgerEntryCommand>(c =>
                    c.ViewerUserId == Viewer && c.Amount == 50 && c.EntryType == "EarnPipeline"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GrantCurrency_rejects_a_non_guid_viewer()
    {
        ICurrencyAccountService accounts = Substitute.For<ICurrencyAccountService>();
        GrantCurrencyAction sut = new(accounts);

        ActionResult result = await sut.ExecuteAsync(Context("anonymous"), Action(("amount", 50)));

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task DeductCurrency_debits_and_stops_on_insufficient_funds()
    {
        ICurrencyAccountService accounts = Substitute.For<ICurrencyAccountService>();
        accounts
            .PostLedgerEntryAsync(
                Channel,
                Arg.Any<PostLedgerEntryCommand>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<CurrencyLedgerEntryDto>("broke", "INSUFFICIENT_FUNDS"));
        DeductCurrencyAction sut = new(accounts);

        ActionResult result = await sut.ExecuteAsync(Context(), Action(("amount", 50)));

        result.Succeeded.Should().BeFalse(); // stops the pipeline
        await accounts
            .Received(1)
            .PostLedgerEntryAsync(
                Channel,
                Arg.Is<PostLedgerEntryCommand>(c =>
                    c.Amount == -50 && c.EntryType == "SpendPipeline"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task CheckBalance_writes_its_variable_and_gates_on_min()
    {
        ICurrencyAccountService accounts = Substitute.For<ICurrencyAccountService>();
        accounts
            .GetBalanceAsync(Channel, Viewer, Arg.Any<CancellationToken>())
            .Returns(Result.Success(30L));
        CheckBalanceAction sut = new(accounts);
        PipelineExecutionContext ctx = Context();

        ActionResult below = await sut.ExecuteAsync(ctx, Action(("min", 50), ("set_var", "coins")));

        ctx.Variables["coins"].Should().Be("30");
        below.Succeeded.Should().BeFalse(); // 30 < 50 gates the pipeline

        ActionResult met = await sut.ExecuteAsync(ctx, Action(("min", 10)));
        met.Succeeded.Should().BeTrue();
        ctx.Variables["balance"].Should().Be("30");
    }

    [Fact]
    public async Task JarContribute_routes_the_contribution_to_the_jar_service()
    {
        ISavingsJarService jars = Substitute.For<ISavingsJarService>();
        jars.ContributeAsync(Channel, Arg.Any<JarContributeRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new JarMovementDto(
                        1,
                        Jar,
                        Channel,
                        Viewer,
                        25,
                        "Contribute",
                        75,
                        1,
                        null,
                        default
                    )
                )
            );
        JarContributeAction sut = new(jars);

        ActionResult result = await sut.ExecuteAsync(
            Context(),
            Action(("jar_id", Jar.ToString()), ("amount", 25))
        );

        result.Succeeded.Should().BeTrue(result.ErrorMessage);
        result.Output.Should().Be("75"); // jar balance after
        await jars.Received(1)
            .ContributeAsync(
                Channel,
                Arg.Is<JarContributeRequest>(c =>
                    c.JarId == Jar && c.ContributorUserId == Viewer && c.Amount == 25
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
