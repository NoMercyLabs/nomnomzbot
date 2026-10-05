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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// Proves the named roll: <c>{roll.NAME.MIN.MAX[.STEP]}</c> draws once and stores the value,
/// <c>{roll.NAME}</c> reads it again anywhere in the same render, and <c>{roll.NAME.complement}</c> is
/// 100 minus it (the "two percents that add up to 100" case).
/// </summary>
public sealed class RollTemplateResolverTests
{
    private const int Draws = 400;
    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-9000-00000000f201");

    private readonly TemplateResolver _resolver;

    public RollTemplateResolverTests()
    {
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(PronounGrammarTestDbContext.New());
        ServiceProvider provider = services.BuildServiceProvider();

        _resolver = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        );
    }

    private Task<string> ResolveAsync(string template) =>
        _resolver.ResolveAsync(template, new Dictionary<string, string>(), Channel);

    private async Task<List<int[]>> DrawAsync(string template)
    {
        List<int[]> rows = [];
        for (int i = 0; i < Draws; i++)
        {
            string resolved = await ResolveAsync(template);
            resolved
                .Should()
                .MatchRegex("^-?[0-9]+(\\|-?[0-9]+)*$", $"'{resolved}' must be numbers");
            rows.Add([.. resolved.Split('|').Select(int.Parse)]);
        }
        return rows;
    }

    [Fact]
    public async Task ANamedRoll_GivesTheSameValueInEveryPlace_AndStaysInRange()
    {
        List<int[]> rows = await DrawAsync("{roll.pct.60.98}|{roll.pct}|{roll.pct}");

        rows.Should().OnlyContain(r => r[0] == r[1] && r[1] == r[2]);
        rows.Should().OnlyContain(r => r[0] >= 60 && r[0] <= 98);
        rows.Select(r => r[0]).Should().Contain(60).And.Contain(98);
    }

    [Fact]
    public async Task TheComplement_AlwaysSumsToOneHundred()
    {
        List<int[]> rows = await DrawAsync("{roll.pct.60.98}|{roll.pct.complement}");

        rows.Should().OnlyContain(r => r[0] + r[1] == 100);
        rows.Select(r => r[1]).Should().Contain(2).And.Contain(40);
    }

    [Fact]
    public async Task TheReadCanComeBeforeTheDefinition()
    {
        List<int[]> rows = await DrawAsync("{roll.pct.complement}|{roll.pct}|{roll.pct.5.95}");

        rows.Should().OnlyContain(r => r[1] == r[2] && r[0] + r[1] == 100);
    }

    [Fact]
    public async Task TheStepForm_OnlyGivesMultiplesOfTheStepFromMin()
    {
        List<int[]> rows = await DrawAsync("{roll.price.50.990.10}|{roll.price}");

        rows.Should().OnlyContain(r => r[0] == r[1] && r[0] >= 50 && r[0] <= 990);
        rows.Should().OnlyContain(r => (r[0] - 50) % 10 == 0);
        rows.Select(r => r[0]).Should().Contain(50).And.Contain(990);
    }

    [Fact]
    public async Task TwoNames_RollIndependently()
    {
        List<int[]> rows = await DrawAsync("{roll.a.1.1000}|{roll.b.1.1000}");

        rows.Should().Contain(r => r[0] != r[1]);
        rows.Should().OnlyContain(r => r[0] >= 1 && r[0] <= 1000 && r[1] >= 1 && r[1] <= 1000);
    }

    [Fact]
    public async Task ARollNeverDefined_IsLeftRaw_AndABadRangeToo()
    {
        (await ResolveAsync("{roll.ghost}")).Should().Be("{roll.ghost}");
        (await ResolveAsync("{roll.ghost.complement}")).Should().Be("{roll.ghost.complement}");
        (await ResolveAsync("{roll.x.9.3}")).Should().Be("{roll.x.9.3}");
    }
}
