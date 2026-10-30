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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// The logic, number and fun helpers in a viewer's own text, through the real parser, registry and
/// <c>ResolveViewerAsync</c>: the safe ones render with their catalog result, a count past the viewer limit is
/// cut to the limit, and a helper that writes or reads channel data stays as written even inside an open branch.
/// </summary>
public sealed class ViewerLogicNumberFunTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000c412");

    private static readonly ViewerTemplateLimits Limits = ViewerTemplateLimits.WithOutputCap(2000);

    /// <summary>Every key of the logic, number and fun groups that stays closed to a viewer, with the reason.</summary>
    private static readonly string[] ClosedKeys = ["fun.8ball", "if.<path>.matches"];

    /// <summary>The provider folders under <c>Helpers</c> this slice classified (random.quote and random.chatter live elsewhere).</summary>
    private static readonly string?[] GroupProviderFolders = ["Logic", "Numbers", "Fun"];

    private static readonly string[] GroupNamespaces =
    [
        "if",
        "switch",
        "chance",
        "coalesce",
        "default",
        "literal",
        "chars",
        "pick",
        "random",
        "math",
        "num",
        "meter",
        "fun",
    ];

    private static Dictionary<string, string> Seeds() =>
        new(StringComparer.OrdinalIgnoreCase) { ["user"] = "ViewerOne" };

    private static ServiceProvider BuildServices(WriteProbe? probe = null)
    {
        ServiceCollection services = new();
        services.AddTemplateHelpers(typeof(TemplateResolver).Assembly);
        if (probe is not null)
            services.AddSingleton(WriteProbeProvider(probe));
        return services.BuildServiceProvider();
    }

    private static TemplateResolver BuildResolver(ServiceProvider services, bool isLive = false)
    {
        IChannelRegistry channels = Substitute.For<IChannelRegistry>();
        channels
            .Get(Channel)
            .Returns(
                new ChannelContext
                {
                    BroadcasterId = Channel,
                    TwitchChannelId = "39863651",
                    ChannelName = "stoney_eagle",
                    IsLive = isLive,
                }
            );
        return new TemplateResolver(
            services.GetRequiredService<IServiceScopeFactory>(),
            channels,
            NullLogger<TemplateResolver>.Instance,
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero)),
            services.GetRequiredService<ITemplateHelperRegistry>()
        );
    }

    private static async Task<ViewerTemplateResult> ResolveAsync(
        string text,
        bool isLive = false,
        WriteProbe? probe = null
    )
    {
        await using ServiceProvider services = BuildServices(probe);
        return await BuildResolver(services, isLive)
            .ResolveViewerAsync(text, Seeds(), Channel, Limits);
    }

    private static async Task<List<string>> ResolveManyAsync(string text, int times)
    {
        await using ServiceProvider services = BuildServices();
        TemplateResolver resolver = BuildResolver(services);
        List<string> results = [];
        for (int i = 0; i < times; i++)
            results.Add((await resolver.ResolveViewerAsync(text, Seeds(), Channel, Limits)).Text);
        return results;
    }

    // ── logic ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(true, "live")]
    [InlineData(false, "offline")]
    public async Task If_over_a_public_stream_field_renders_the_branch_that_holds(
        bool isLive,
        string expected
    )
    {
        ViewerTemplateResult result = await ResolveAsync(
            "{if:{stream.isLive}|live|offline}",
            isLive
        );

        result.Text.Should().Be(expected);
        result.Refusal.Should().Be(ViewerTemplateRefusal.None);
    }

    [Theory]
    [InlineData("{if.not:no|shown|hidden}", "shown")]
    [InlineData("{if.all:yes|yes|both|not both}", "both")]
    [InlineData("{if.any:no|yes|one|none}", "one")]
    [InlineData("{if.empty:|blank|filled}", "blank")]
    [InlineData("{switch:b|a=1|b=2|3}", "2")]
    [InlineData("{switch.range:5|3=low|10=mid|high}", "mid")]
    [InlineData("{chance:100|hit|miss}", "hit")]
    [InlineData("{chance:0|hit|miss}", "miss")]
    [InlineData("{coalesce:|  |ViewerTwo}", "ViewerTwo")]
    [InlineData("{default:|fallback}", "fallback")]
    [InlineData("{literal:{user}}", "{user}")]
    [InlineData("a{chars.pipe}b", "a|b")]
    [InlineData("a{chars.space}b", "a b")]
    public async Task The_other_logic_helpers_render_their_catalog_result(
        string text,
        string expected
    )
    {
        ViewerTemplateResult result = await ResolveAsync(text);

        result.Text.Should().Be(expected);
    }

    [Fact]
    public async Task If_over_the_viewers_own_name_branches_on_it()
    {
        ViewerTemplateResult result = await ResolveAsync(
            "{if.user.equals:ViewerOne:hello {user}|who?}"
        );

        result.Text.Should().Be("hello ViewerOne");
    }

    [Fact]
    public async Task The_regex_operator_stays_as_written_in_a_viewers_text_and_runs_for_the_broadcaster()
    {
        const string text = "{if.user.matches:^View:yes|no}";
        await using ServiceProvider services = BuildServices();
        TemplateResolver resolver = BuildResolver(services);

        ViewerTemplateResult viewer = await resolver.ResolveViewerAsync(
            text,
            Seeds(),
            Channel,
            Limits
        );
        string broadcaster = await resolver.ResolveAsync(text, Seeds(), Channel);

        viewer.Text.Should().Be(text);
        broadcaster.Should().Be("yes");
    }

    [Fact]
    public async Task Pick_renders_one_of_its_items_and_every_item_comes_up()
    {
        List<string> draws = await ResolveManyAsync("{pick:a|b|c}", 60);

        draws.Should().OnlyContain(draw => draw == "a" || draw == "b" || draw == "c");
        draws.Distinct().Should().BeEquivalentTo(["a", "b", "c"]);
    }

    [Fact]
    public async Task Pick_in_a_viewers_text_keeps_no_no_repeat_memory_keyed_by_the_viewers_text()
    {
        List<string> draws = await ResolveManyAsync("{pick:x|y}", 200);

        draws.Zip(draws.Skip(1)).Should().Contain(pair => pair.First == pair.Second);
    }

    [Fact]
    public async Task Pick_inline_pick_n_pick_weighted_and_shuffle_stay_inside_their_items()
    {
        (await ResolveAsync("{pick.inline:a;b}")).Text.Should().BeOneOf("a", "b");
        (await ResolveAsync("{pick.weighted:0=never|1=always}")).Text.Should().Be("always");
        (await ResolveAsync("{pick.n:5|a|b}")).Text.Should().BeOneOf("a, b", "b, a");
        (await ResolveAsync("{pick.shuffle:a|b}")).Text.Should().BeOneOf("a, b", "b, a");
    }

    [Fact]
    public async Task Random_helpers_render_a_value_in_their_range()
    {
        (await ResolveAsync("{random.number:5|7}")).Text.Should().BeOneOf("5", "6", "7");
        (await ResolveAsync("{random.bool}")).Text.Should().BeOneOf("true", "false");
        (await ResolveAsync("{random.decimal:1|1|2}")).Text.Should().Be("1.00");
        (await ResolveAsync("{random.letter}")).Text.Should().MatchRegex("^[A-Z]$");
        (await ResolveAsync("{random.color}")).Text.Should().MatchRegex("^#[0-9A-F]{6}$");
    }

    // ── numbers and math ────────────────────────────────────────────────────

    [Theory]
    [InlineData("{math.sqrt:9}", "3")]
    [InlineData("{math:2+3*4}", "14")]
    [InlineData("{math.add:2|3}", "5")]
    [InlineData("{math.sum:1|2|3}", "6")]
    [InlineData("{num.ordinal:3}", "3rd")]
    [InlineData("{num.words:42}", "forty-two")]
    [InlineData("{num.format:1234567}", "1,234,567")]
    [InlineData("{num.round:2.456|2}", "2.46")]
    [InlineData("{num.clamp:150|0|100}", "100")]
    [InlineData("{num.percent:41|50}", "82%")]
    [InlineData("{num.duration:8100}", "2h 15m")]
    [InlineData("{num.convert:10|km|mi}", "6.21")]
    [InlineData("{num.roman:2026}", "MMXXVI")]
    [InlineData("{num.hex:255}", "FF")]
    [InlineData("{num.bar:6|10}", "██████░░░░")]
    [InlineData("{meter:60}", "██████░░░░ 60%")]
    [InlineData("{meter.stars:3|5}", "★★★☆☆")]
    public async Task A_number_or_math_helper_renders_its_catalog_example(
        string text,
        string expected
    )
    {
        ViewerTemplateResult result = await ResolveAsync(text);

        result.Text.Should().Be(expected);
    }

    [Fact]
    public async Task A_meter_wider_than_the_viewer_limit_is_cut_to_the_limit_not_refused()
    {
        ViewerTemplateResult plain = await ResolveAsync("{meter.plain:100|50}");
        ViewerTemplateResult withNumber = await ResolveAsync("{meter:100|50}");
        ViewerTemplateResult inside = await ResolveAsync("{meter.plain:50|10}");

        plain.Text.Should().Be(new string('█', 20));
        withNumber.Text.Should().Be(new string('█', 20) + " 100%");
        inside.Text.Should().Be("█████░░░░░");
    }

    [Fact]
    public async Task The_broadcaster_surface_keeps_the_wide_meter()
    {
        await using ServiceProvider services = BuildServices();
        string resolved = await BuildResolver(services)
            .ResolveAsync("{meter.plain:100|50}", Seeds(), Channel);

        resolved.Should().Be(new string('█', 50));
    }

    // ── fun ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Coin_and_dice_render_a_value_in_their_range()
    {
        List<string> coins = await ResolveManyAsync("{fun.coin}", 40);
        List<string> dice = await ResolveManyAsync("{fun.dice:2d6}", 60);
        List<string> faces = await ResolveManyAsync("{fun.dice.emoji}", 30);

        coins.Should().OnlyContain(coin => coin == "heads" || coin == "tails");
        coins.Distinct().Should().BeEquivalentTo(["heads", "tails"]);
        dice.Select(int.Parse).Should().OnlyContain(total => total >= 2 && total <= 12);
        faces.Should().OnlyContain(face => "⚀⚁⚂⚃⚄⚅".Contains(face));
    }

    [Fact]
    public async Task More_dice_than_the_viewer_limit_are_rolled_as_the_limit()
    {
        ViewerTemplateResult detail = await ResolveAsync("{fun.dice.detail:50d2}");
        ViewerTemplateResult total = await ResolveAsync("{fun.dice:150d2}");
        ViewerTemplateResult faces = await ResolveAsync("{fun.dice.emoji:25}");
        ViewerTemplateResult countdown = await ResolveAsync("{fun.countdown:25}");

        string[] terms = detail.Text.Split(" = ")[0].Split(" + ");
        terms.Should().HaveCount(20);
        terms.Should().OnlyContain(term => term == "1" || term == "2");
        int.Parse(total.Text).Should().BeInRange(20, 40);
        faces.Text.Split(' ').Should().HaveCount(20);
        countdown.Text.Split(' ').Should().HaveCount(20);
        countdown.Text.Should().StartWith("20… 19…").And.EndWith("2… 1…");
    }

    [Fact]
    public async Task The_broadcaster_surface_still_refuses_dice_past_the_hard_cap()
    {
        await using ServiceProvider services = BuildServices();
        string resolved = await BuildResolver(services)
            .ResolveAsync("{fun.dice:150d2}", Seeds(), Channel);

        resolved.Should().Be("{fun.dice:150d2}");
    }

    [Fact]
    public async Task The_other_pure_fun_helpers_render_for_a_viewer()
    {
        (await ResolveAsync("{fun.rps.vs:rock}"))
            .Text.Should()
            .MatchRegex("^(rock — a draw|paper — you lose|scissors — you win)$");
        (await ResolveAsync("{fun.card}")).Text.Should().MatchRegex("^(A|[2-9]|10|J|Q|K)[♠♥♦♣]$");
        (await ResolveAsync("{fun.roulette}")).Text.Should().BeOneOf("click", "bang");
        (await ResolveAsync("{fun.shrug}")).Text.Should().Be("¯\\_(ツ)_/¯");
        (await ResolveAsync("{fun.ship:Ann|Bob}")).Text.Should().StartWith("Ann + Bob = ");
        (await ResolveAsync("{fun.percent:pizza}")).Text.Should().MatchRegex("^[0-9]{1,3}%$");
        (await ResolveAsync("{fun.random.food}")).Text.Should().MatchRegex(@"^\S+ [a-z ]+$");
    }

    // ── what stays closed ───────────────────────────────────────────────────

    [Fact]
    public async Task A_helper_that_writes_inside_an_open_branch_is_not_run_and_changes_nothing()
    {
        WriteProbe probe = new();

        ViewerTemplateResult viaIf = await ResolveAsync(
            "{if:true|{probe.write:1}|no}",
            probe: probe
        );
        ViewerTemplateResult viaPick = await ResolveAsync(
            "{pick.inline:{probe.write:2}}",
            probe: probe
        );
        ViewerTemplateResult viaChance = await ResolveAsync(
            "{chance:100|{probe.write:3}|no}",
            probe: probe
        );

        probe.Writes.Should().BeEmpty();
        viaIf.Text.Should().Be("{probe.write:1}");
        viaPick.Text.Should().Be("{probe.write:2}");
        viaChance.Text.Should().Be("{probe.write:3}");
    }

    [Fact]
    public async Task The_set_helper_inside_an_open_branch_stays_as_written_and_sets_nothing()
    {
        ViewerTemplateResult result = await ResolveAsync("{if:true|{set:x=1}|no}{math:2+2}");

        result.Text.Should().Be("{set:x=1}4");
    }

    [Theory]
    [InlineData("{if:true|{include.cmd:hug}|no}", "{include.cmd:hug}")]
    [InlineData("{switch:a|a={count.visits}|b}", "{count.visits}")]
    [InlineData("{default:|{viewer.data.score}}", "{viewer.data.score}")]
    [InlineData("{fun.8ball}", "{fun.8ball}")]
    [InlineData("{math.add:{count.visits}|1}", "{math.add:{count.visits}|1}")]
    public async Task A_closed_helper_in_or_around_an_open_one_stays_as_written(
        string text,
        string expected
    )
    {
        ViewerTemplateResult result = await ResolveAsync(text);

        result.Text.Should().Be(expected);
    }

    [Fact]
    public async Task Nesting_past_the_viewer_depth_is_still_refused_with_the_open_helpers()
    {
        const string text = "{if:true|{if:true|{if:true|{if:true|deep|x}|x}|x}|x}";

        ViewerTemplateResult result = await ResolveAsync(text);

        result.Refusal.Should().Be(ViewerTemplateRefusal.TooDeep);
        result.Text.Should().Be(text);
    }

    // ── the classification itself ───────────────────────────────────────────

    [Fact]
    public async Task Every_logic_number_and_fun_key_is_open_to_a_viewer_except_the_named_closed_ones()
    {
        await using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();
        List<TemplateHelperEntry> group =
        [
            .. registry.All.Where(entry =>
                GroupNamespaces.Contains(entry.Key.Split('.')[0], StringComparer.Ordinal)
                && GroupProviderFolders.Contains(
                    registry.ProviderFor(entry.Key)?.GetType().Namespace?.Split('.')[^1]
                )
            ),
        ];

        List<string> closed =
        [
            .. group
                .Where(entry => !entry.Contexts.Contains(TemplateHelperContext.Viewer))
                .Select(entry => entry.Key),
        ];

        closed.Should().BeEquivalentTo(ClosedKeys);
        group
            .Count(entry => entry.Contexts.Contains(TemplateHelperContext.Viewer))
            .Should()
            .Be(group.Count - ClosedKeys.Length);
        group.Should().HaveCount(120);
    }

    /// <summary>Counts what a helper wrote; stands in for a helper that changes channel data.</summary>
    private sealed class WriteProbe
    {
        public List<string> Writes { get; } = [];
    }

    /// <summary>
    /// A substitute, not a class: a concrete provider in this assembly would be picked up by every test that scans
    /// the test assembly for template helper providers.
    /// </summary>
    private static ITemplateHelperProvider WriteProbeProvider(WriteProbe probe)
    {
        ITemplateHelperProvider provider = Substitute.For<ITemplateHelperProvider>();
        provider.Namespace.Returns("probe");
        provider.Entries.Returns([
            new TemplateHelperEntry(
                "probe.write",
                TemplateHelperContexts.Standard,
                new NomNomzBot.Application.Abstractions.Localization.LocalizedText(
                    "template.helper.probe.write"
                )
            ),
        ]);
        provider
            .PrepareAsync(
                Arg.Any<TemplateRenderScope>(),
                Arg.Any<IReadOnlyCollection<HelperNode>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.CompletedTask);
        provider
            .RenderAsync(
                Arg.Any<TemplateRenderScope>(),
                Arg.Any<HelperNode>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                probe.Writes.Add(call.ArgAt<HelperNode>(1).Raw);
                return ValueTask.FromResult<string?>("written");
            });
        return provider;
    }
}
