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
/// Proves <c>{random.number.&lt;min&gt;.&lt;max&gt;[.&lt;step&gt;]}</c> draws inside its range, on its step,
/// reaches both ends, and that the old <c>{random.number.&lt;n&gt;}</c> (1..n) form still works.
/// </summary>
public sealed class RandomNumberTemplateResolverTests
{
    private const int Draws = 600;
    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-9000-00000000f101");

    private readonly TemplateResolver _resolver;

    public RandomNumberTemplateResolverTests()
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

    private async Task<List<int>> DrawAsync(string template)
    {
        List<int> values = [];
        for (int i = 0; i < Draws; i++)
        {
            string resolved = await _resolver.ResolveAsync(
                template,
                new Dictionary<string, string>(),
                Channel
            );
            int.TryParse(resolved, out int value)
                .Should()
                .BeTrue($"'{resolved}' (from {template}) must be a whole number");
            values.Add(value);
        }
        return values;
    }

    [Theory]
    [InlineData(3, 47)]
    [InlineData(7, 41)]
    [InlineData(60, 98)]
    [InlineData(3, 24)]
    public async Task RangeForm_StaysInsideMinMax(int min, int max)
    {
        List<int> values = await DrawAsync($"{{random.number.{min}.{max}}}");

        values.Should().OnlyContain(v => v >= min && v <= max);
    }

    [Theory]
    [InlineData(50, 990, 10)]
    [InlineData(200, 900, 100)]
    public async Task StepForm_OnlyGivesMultiplesOfTheStepFromMin(int min, int max, int step)
    {
        List<int> values = await DrawAsync($"{{random.number.{min}.{max}.{step}}}");

        values.Should().OnlyContain(v => v >= min && v <= max && (v - min) % step == 0);
    }

    [Theory]
    [InlineData(3, 7, 1, new[] { 3, 4, 5, 6, 7 })]
    [InlineData(200, 900, 100, new[] { 200, 300, 400, 500, 600, 700, 800, 900 })]
    [InlineData(0, 25, 10, new[] { 0, 10, 20 })]
    public void Draw_EveryReachableStep_IsTheExactGrid_AndReachesBothEnds(
        int min,
        int max,
        int step,
        int[] expectedGrid
    )
    {
        RandomNumberSpec spec = new(min, max, step);

        List<int> reached = Enumerable
            .Range(0, expectedGrid.Length)
            .Select(k => spec.Draw(new ScriptedRandom(k)))
            .ToList();

        reached.Should().Equal(expectedGrid);
    }

    [Fact]
    public void Draw_AsksForTheWholeStepCount_SoTheTopStepIsReachable()
    {
        ScriptedRandom random = new(0);

        new RandomNumberSpec(50, 990, 10).Draw(random);

        random.LastMin.Should().Be(0);
        random.LastMaxExclusive.Should().Be(95, "50..990 on step 10 is 95 values");
    }

    [Fact]
    public async Task OldSingleArgumentForm_StaysInsideOneToN()
    {
        List<int> values = await DrawAsync("{random.number.6}");

        values.Should().OnlyContain(v => v >= 1 && v <= 6);
    }

    private sealed class ScriptedRandom(int next) : Random
    {
        public int LastMin { get; private set; }
        public int LastMaxExclusive { get; private set; }

        public override int Next(int minValue, int maxValue)
        {
            LastMin = minValue;
            LastMaxExclusive = maxValue;
            return next;
        }
    }

    [Fact]
    public async Task MaxBelowMin_IsLeftRawSoTheAuthorSeesTheMistake()
    {
        string resolved = await _resolver.ResolveAsync(
            "{random.number.9.3}",
            new Dictionary<string, string>(),
            Channel
        );

        resolved.Should().Be("{random.number.9.3}");
    }
}
