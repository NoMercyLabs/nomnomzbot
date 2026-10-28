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
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Logic;
using NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Text;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Logic;

/// <summary>
/// The lazy logic rows run only the branch they choose: a spy helper counts how often it rendered, and every
/// case asserts both the text and that count.
/// </summary>
public sealed class LogicHelpersLazyTests
{
    [Theory]
    [InlineData("{if:{user.isSub}|chosen|{logicspy.hit}}", "chosen")]
    [InlineData("{if:{user.isVip}|{logicspy.hit}|chosen}", "chosen")]
    [InlineData("{if.not:{user.isSub}|{logicspy.hit}|chosen}", "chosen")]
    [InlineData("{if.stream.live:chosen|{logicspy.hit}}", "chosen")]
    [InlineData("{if.user.isVip:{logicspy.hit}|chosen}", "chosen")]
    [InlineData("{if.user.login.equals:nobody:{logicspy.hit}|chosen}", "chosen")]
    [InlineData("{if.user.login.equals:viewerone:chosen|{logicspy.hit}}", "chosen")]
    [InlineData("{if.all:{user.isVip}|{logicspy.hit}|a|chosen}", "chosen")]
    [InlineData("{if.any:{user.isSub}|{logicspy.hit}|chosen|b}", "chosen")]
    [InlineData("{if.empty:{user.login}|{logicspy.hit}|chosen}", "chosen")]
    [InlineData("{default:chosen|{logicspy.hit}}", "chosen")]
    [InlineData("{coalesce:chosen|{logicspy.hit}}", "chosen")]
    [InlineData("{switch:b|a={logicspy.hit}|b=chosen|c={logicspy.hit}|{logicspy.hit}}", "chosen")]
    [InlineData("{switch:z|a={logicspy.hit}|{logicspy.hit}}", "hit")]
    [InlineData("{switch.range:3|5=chosen|9={logicspy.hit}|{logicspy.hit}}", "chosen")]
    [InlineData("{chance:100|chosen|{logicspy.hit}}", "chosen")]
    [InlineData("{chance:0|{logicspy.hit}|chosen}", "chosen")]
    [InlineData("{literal:{logicspy.hit}}", "{logicspy.hit}")]
    public async Task The_branch_that_is_not_chosen_never_renders(string template, string expected)
    {
        LogicSpyProvider spy = new();

        string rendered = await RenderAsync(template, spy, new Random(1));

        rendered.Should().Be(expected);
        spy.Hits.Should().Be(expected == "hit" ? 1 : 0);
    }

    [Theory]
    [InlineData("{if:{user.isVip}|a|{logicspy.hit}}", "hit")]
    [InlineData("{if.user.login.equals:viewerone:{logicspy.hit}|b}", "hit")]
    [InlineData("{default:{args.5}|{logicspy.hit}}", "hit")]
    [InlineData("{coalesce:{args.5}|{logicspy.hit}|{logicspy.hit}}", "hit")]
    [InlineData("{switch:b|a=x|b={logicspy.hit}}", "hit")]
    [InlineData("{chance:100|{logicspy.hit}|b}", "hit")]
    public async Task The_chosen_branch_renders_exactly_once(string template, string expected)
    {
        LogicSpyProvider spy = new();

        string rendered = await RenderAsync(template, spy, new Random(1));

        rendered.Should().Be(expected);
        spy.Hits.Should().Be(1);
    }

    [Fact]
    public async Task A_nested_if_resolves_only_the_inner_branch_that_is_chosen()
    {
        LogicSpyProvider spy = new();

        string rendered = await RenderAsync(
            "{if.user.isSub:{if.user.isVip:{logicspy.hit}|sub only}|{logicspy.hit}}",
            spy,
            new Random(1)
        );

        rendered.Should().Be("sub only");
        spy.Hits.Should().Be(0);
    }

    private static async Task<string> RenderAsync(
        string template,
        LogicSpyProvider spy,
        Random random
    )
    {
        TemplateHelperRegistryAggregate registry = new([
            new IfTemplateHelpers(),
            new DefaultTemplateHelpers(),
            new CoalesceTemplateHelpers(),
            new SwitchTemplateHelpers(),
            new ChanceTemplateHelpers(random),
            new LiteralTemplateHelpers(),
            spy,
        ]);
        return await TextHelpersTableTests
            .BuildResolver(registry)
            .ResolveAsync(template, LogicHelpersTableTests.Fixture, broadcasterId: null);
    }

    /// <summary>A helper that counts how often it rendered, so a branch that never ran shows.</summary>
    private sealed class LogicSpyProvider : ITemplateHelperProvider
    {
        public int Hits { get; private set; }

        public string Namespace => "logicspy";

        public IReadOnlyList<TemplateHelperEntry> Entries { get; } =
        [
            new(
                "logicspy.hit",
                [TemplateHelperContext.Command],
                new LocalizedText("template.helper.logicspy.hit")
            ),
        ];

        public ValueTask PrepareAsync(
            TemplateRenderScope scope,
            IReadOnlyCollection<HelperNode> nodes,
            CancellationToken cancellationToken
        ) => ValueTask.CompletedTask;

        public ValueTask<string?> RenderAsync(
            TemplateRenderScope scope,
            HelperNode node,
            CancellationToken cancellationToken
        )
        {
            Hits++;
            return ValueTask.FromResult<string?>("hit");
        }
    }
}
