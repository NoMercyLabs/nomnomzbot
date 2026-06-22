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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.DTOs.Billing;
using NomNomzBot.Infrastructure.CustomCode;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// Proves the sandbox exec-ms meter (custom-code.md §3.3) is a faithful adapter over the billing usage meter: the
/// pre-run check maps the billing verdict to a QuotaCheck, and the post-run record accumulates elapsed ms under the
/// sandbox_exec_ms metric (skipping a zero-ms run).
/// </summary>
public sealed class ScriptExecutionMeterTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000d001");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static (ScriptExecutionMeter Sut, IUsageMeteringService Usage) Build()
    {
        IUsageMeteringService usage = Substitute.For<IUsageMeteringService>();
        return (new ScriptExecutionMeter(usage, new FakeTimeProvider(Now)), usage);
    }

    [Fact]
    public async Task CheckSandboxBudget_maps_the_billing_verdict()
    {
        (ScriptExecutionMeter sut, IUsageMeteringService usage) = Build();
        usage
            .CheckAsync(
                Arg.Any<Guid>(),
                "sandbox_exec_ms",
                Arg.Any<long>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new QuotaCheckDto(true, "sandbox_exec_ms", 100, 1000, 900)));

        QuotaCheck verdict = (await sut.CheckSandboxBudgetAsync(Channel)).Value;

        verdict.Allowed.Should().BeTrue();
        verdict.LimitMs.Should().Be(1000);
        verdict.UsedMs.Should().Be(100);
    }

    [Fact]
    public async Task RecordSandboxUsage_accumulates_under_the_sandbox_metric()
    {
        (ScriptExecutionMeter sut, IUsageMeteringService usage) = Build();
        usage
            .RecordAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        await sut.RecordSandboxUsageAsync(Channel, 50, "exec-1");

        await usage
            .Received()
            .RecordAsync(Channel, "sandbox_exec_ms", 50, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordSandboxUsage_skips_a_zero_ms_run()
    {
        (ScriptExecutionMeter sut, IUsageMeteringService usage) = Build();

        await sut.RecordSandboxUsageAsync(Channel, 0, "exec-1");

        await usage
            .DidNotReceive()
            .RecordAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>()
            );
    }
}
