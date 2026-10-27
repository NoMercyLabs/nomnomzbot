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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Parsing;

/// <summary>
/// The rendered text of the parser rules (catalog section 3) through the real resolver, with the seed
/// variables of the fixed fixture (catalog section 5): the user is ViewerOne.
/// </summary>
public sealed class TemplateResolverParsingTests
{
    private static readonly Dictionary<string, string> Seeds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["user"] = "ViewerOne",
        ["user.name"] = "viewerone",
    };

    private static TemplateResolver BuildResolver() =>
        new(
            new ServiceCollection()
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 26, 14, 30, 0, TimeSpan.Zero))
        );

    [Theory]
    [InlineData("{{user}}", "{ViewerOne}")]
    [InlineData("{transform.upper:{user}}", "VIEWERONE")]
    [InlineData("{transform.upper:hi {user.name}!}", "HI VIEWERONE!")]
    [InlineData("{a {user}", "{a ViewerOne")]
    [InlineData("{nope} and {user}", "{nope} and ViewerOne")]
    [InlineData(@"\{user\} is {user}", "{user} is ViewerOne")]
    [InlineData(@"a\\b \| c\:d", @"a\b | c:d")]
    public async Task Async_render_follows_the_parser_rules(string template, string expected)
    {
        string rendered = await BuildResolver().ResolveAsync(template, Seeds, broadcasterId: null);

        rendered.Should().Be(expected);
    }

    [Theory]
    [InlineData("{{user}}", "{ViewerOne}")]
    [InlineData("{transform.upper:{user}}", "VIEWERONE")]
    [InlineData(@"\{user\} is {user}", "{user} is ViewerOne")]
    public void Sync_render_follows_the_parser_rules(string template, string expected)
    {
        string rendered = BuildResolver().Resolve(template, Seeds);

        rendered.Should().Be(expected);
    }

    [Fact]
    public async Task A_helper_nested_nine_deep_stays_as_written_while_the_eight_around_it_run()
    {
        string template =
            string.Concat(Enumerable.Repeat("{transform.upper:", 9)) + "a" + new string('}', 9);

        string rendered = await BuildResolver().ResolveAsync(template, Seeds, broadcasterId: null);

        rendered.Should().Be("{TRANSFORM.UPPER:A}");
    }
}
