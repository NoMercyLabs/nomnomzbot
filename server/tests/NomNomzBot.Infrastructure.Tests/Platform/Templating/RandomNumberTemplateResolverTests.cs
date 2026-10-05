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
    public async Task RangeForm_StaysInsideMinMax_AndReachesBothEnds(int min, int max)
    {
        List<int> values = await DrawAsync($"{{random.number.{min}.{max}}}");

        values.Should().OnlyContain(v => v >= min && v <= max);
        values.Should().Contain(min);
        values.Should().Contain(max);
    }

    [Theory]
    [InlineData(50, 990, 10)]
    [InlineData(200, 900, 100)]
    public async Task StepForm_OnlyGivesMultiplesOfTheStepFromMin_AndReachesBothEnds(
        int min,
        int max,
        int step
    )
    {
        List<int> values = await DrawAsync($"{{random.number.{min}.{max}.{step}}}");

        values.Should().OnlyContain(v => v >= min && v <= max && (v - min) % step == 0);
        values.Should().Contain(min);
        values.Should().Contain(max);
    }

    [Fact]
    public async Task OldSingleArgumentForm_StillGivesOneToN()
    {
        List<int> values = await DrawAsync("{random.number.6}");

        values.Should().OnlyContain(v => v >= 1 && v <= 6);
        values.Should().Contain(1);
        values.Should().Contain(6);
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
