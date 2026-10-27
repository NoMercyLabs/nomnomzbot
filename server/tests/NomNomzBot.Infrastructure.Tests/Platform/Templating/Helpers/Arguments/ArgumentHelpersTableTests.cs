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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Text;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Arguments;

/// <summary>
/// Catalog section 7.16, the <c>args</c> rows: every Example renders to exactly its Output cell against the
/// section 5 fixture (<c>!cmd @ViewerTwo hello big world</c>) through the real parser, registry and resolver.
/// </summary>
public sealed class ArgumentHelpersTableTests
{
    private const string Fixture = "@ViewerTwo hello big world";

    public static TheoryData<string, string> FixtureRows =>
        new()
        {
            { "{args.count}", "4" },
            { "{args}", "@ViewerTwo hello big world" },
            { "{args.1}", "@ViewerTwo" },
            { "{args.rest}", "hello big world" },
            { "{args.2+}", "hello big world" },
            { "{args.2-3}", "hello big" },
            { "{args.last}", "world" },
            { "{args.first}", "@ViewerTwo" },
            { "{args.1.clean}", "ViewerTwo" },
            { "{args.3.number}", "" },
            { "{args.3.number:1}", "1" },
            { "{if:{args.2.isNumber}|n|word}", "word" },
            { "{if:{args.1.isMention}|mention|text}", "mention" },
            { "{args.5:nobody}", "nobody" },
            { "{args.2:nobody}", "hello" },
            { "{args.2.upper}", "HELLO" },
            { "{args.2.length}", "5" },
            { "{args.all.quoted}", "\"@ViewerTwo hello big world\"" },
            { "{args.all.urlEncoded}", "%40ViewerTwo%20hello%20big%20world" },
            { "{args.2.urlEncoded}", "hello" },
            { "{if:{args.isEmpty}|usage: !cmd <name>|ok}", "ok" },
            { "{args.mentions}", "@ViewerTwo" },
            { "{args.mentions.count}", "1" },
            { "{args.numbers}", "" },
            { "{text.upper:{args.rest}}", "HELLO BIG WORLD" },
        };

    public static TheoryData<string, string, string> OtherCommands =>
        new()
        {
            { "set 5m 42 3.5 @A @b", "{args.duration:2}", "300" },
            { "set 5m 42 3.5 @A @b", "{args.3.number}", "42" },
            { "set 5m 42 3.5 @A @b", "{args.4.number:0}", "3.5" },
            { "set 5m 42 3.5 @A @b", "{args.3.isNumber}", "true" },
            { "set 5m 42 3.5 @A @b", "{args.numbers}", "42 3.5" },
            { "set 5m 42 3.5 @A @b", "{args.mentions}", "@A @b" },
            { "set 5m 42 3.5 @A @b", "{args.mentions.count}", "2" },
            { "set 5m 42 3.5 @A @b", "{args.1.isNumber}", "false" },
            { "x 1h30m", "{args.duration:2}", "5400" },
            { "x 90", "{args.duration:2}", "90" },
            { "x 2d", "{args.duration:2}", "172800" },
            { "x 1w", "{args.duration:2}", "604800" },
            { "x 45s", "{args.duration:2}", "45" },
            { "x 1h30", "{args.duration:2}", "{args.duration:2}" },
            { "x soon", "{args.duration:2}", "{args.duration:2}" },
            { "x 5m", "{args.duration:3}", "{args.duration:3}" },
            { "x 5m", "{args.duration:0}", "{args.duration:0}" },
            { "x 5m", "{args.duration}", "{args.duration}" },
            { Fixture, "{args.3+}", "big world" },
            { Fixture, "{args.9+}", "" },
            { Fixture, "{args.2-9}", "hello big world" },
            { Fixture, "{args.1-1}", "@ViewerTwo" },
            { Fixture, "{args.3-2}", "{args.3-2}" },
            { Fixture, "{args.0+}", "{args.0+}" },
            { Fixture, "{args.9.number:7}", "7" },
            { Fixture, "{args.9.number}", "" },
            { Fixture, "{args.9.isNumber}", "false" },
            { Fixture, "{args.9.isMention}", "false" },
            { Fixture, "{args.9.upper}", "{args.9.upper}" },
            { Fixture, "{args.9.clean}", "{args.9.clean}" },
            { Fixture, "{args.9}", "{args.9}" },
            { Fixture, "{args.9:}", "" },
            { "héllo 👍", "{args.2.length}", "1" },
            { "héllo 👍", "{args.1.length}", "5" },
            { "a b", "{args.3.upper:x}", "{args.3.upper:x}" },
            { "", "{args.isEmpty}", "true" },
            { "", "{args.count}", "0" },
            { "", "{args.rest}", "" },
            { "", "{args.first}", "" },
            { "", "{args.last}", "" },
            { "", "{args.all.quoted}", "\"\"" },
            { "", "{args.mentions.count}", "0" },
            { "", "{args.2.upper}", "{args.2.upper}" },
        };

    [Theory]
    [MemberData(nameof(FixtureRows))]
    public async Task A_fixture_row_renders_exactly_its_output(string template, string expected) =>
        (await Render(template, Fixture)).Should().Be(expected);

    [Theory]
    [MemberData(nameof(OtherCommands))]
    public async Task A_row_reads_the_words_of_the_command_it_runs_in(
        string arguments,
        string template,
        string expected
    ) => (await Render(template, arguments)).Should().Be(expected);

    [Theory]
    [InlineData("{args.rest}")]
    [InlineData("{args.2+}")]
    [InlineData("{args.1.upper}")]
    [InlineData("{args.isEmpty}")]
    [InlineData("{args.duration:1}")]
    public async Task A_render_with_no_command_prints_the_token_as_written(string template)
    {
        string rendered = await Resolve(template, new Dictionary<string, string>());

        rendered.Should().Be(template);
    }

    private static Task<string> Render(string template, string arguments) =>
        Resolve(template, ChatMessageHandler.BuildArgumentVariables(arguments));

    private static async Task<string> Resolve(string template, Dictionary<string, string> seeds)
    {
        await using ServiceProvider services = new ServiceCollection()
            .AddTemplateHelpers(typeof(TemplateResolver).Assembly)
            .BuildServiceProvider();
        return await TextHelpersTableTests
            .BuildResolver(services.GetRequiredService<ITemplateHelperRegistry>())
            .ResolveAsync(template, seeds, broadcasterId: null);
    }
}
