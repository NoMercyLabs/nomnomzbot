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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Text;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// The viewer surface of the template engine through the real parser, registry and resolver: the helpers the
/// Viewer row lists render, everything else prints as the viewer wrote it, and the limits hold.
/// </summary>
public sealed class ViewerTemplateResolverTests
{
    private static readonly ViewerTemplateLimits Limits = ViewerTemplateLimits.WithOutputCap(500);

    private static readonly Dictionary<string, string> ViewerSeeds = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["user"] = "ViewerOne",
    };

    private static ServiceProvider BuildServices() =>
        new ServiceCollection()
            .AddTemplateHelpers(typeof(TemplateResolver).Assembly)
            .BuildServiceProvider();

    private static async Task<ViewerTemplateResult> ResolveAsync(
        string text,
        ViewerTemplateLimits? limits = null
    )
    {
        await using ServiceProvider services = BuildServices();
        TemplateResolver resolver = TextHelpersTableTests.BuildResolver(
            services.GetRequiredService<ITemplateHelperRegistry>()
        );
        return await resolver.ResolveViewerAsync(
            text,
            new Dictionary<string, string>(ViewerSeeds, StringComparer.OrdinalIgnoreCase),
            Guid.Parse("0192a000-0000-7000-8000-00000000c411"),
            limits ?? Limits
        );
    }

    [Fact]
    public async Task A_repeat_past_the_copy_limit_makes_exactly_the_limit_of_copies()
    {
        ViewerTemplateResult result = await ResolveAsync("{text.repeat:30|ha}");

        result.Refusal.Should().Be(ViewerTemplateRefusal.None);
        result.Text.Should().Be(string.Concat(Enumerable.Repeat("ha", 20)));
        result.Text.Should().HaveLength(40);
    }

    [Fact]
    public async Task A_repeat_inside_the_limit_is_not_clamped()
    {
        ViewerTemplateResult result = await ResolveAsync("{text.repeat:3|ha}");

        result.Text.Should().Be("hahaha");
    }

    [Fact]
    public async Task A_pad_past_the_limit_is_clamped_to_the_limit()
    {
        ViewerTemplateResult result = await ResolveAsync("{text.pad.left:7|50|0}");

        result.Text.Should().Be(new string('0', 19) + "7");
    }

    [Fact]
    public async Task The_viewers_own_name_and_a_text_helper_over_it_resolve()
    {
        ViewerTemplateResult result = await ResolveAsync("hi {text.upper:{user}}!");

        result.Text.Should().Be("hi VIEWERONE!");
        result.Refusal.Should().Be(ViewerTemplateRefusal.None);
    }

    [Theory]
    [InlineData("{include.cmd:hug}")]
    [InlineData("{set:x|1}")]
    [InlineData("{count.visits}")]
    [InlineData("{viewer.data.score}")]
    [InlineData("{date:yyyy}")]
    [InlineData("{math.sqrt:9}")]
    [InlineData("{not.a.helper}")]
    public async Task A_helper_outside_the_viewer_row_stays_as_written(string text)
    {
        ViewerTemplateResult result = await ResolveAsync($"a {text} b");

        result.Text.Should().Be($"a {text} b");
        result.Refusal.Should().Be(ViewerTemplateRefusal.None);
    }

    [Fact]
    public async Task A_refused_helper_inside_a_text_helper_stays_as_written_while_the_text_helper_renders()
    {
        ViewerTemplateResult result = await ResolveAsync("{text.upper:{include.cmd:hug} ok}");

        result.Text.Should().Be("{INCLUDE.CMD:HUG} OK");
    }

    [Fact]
    public async Task Nesting_three_deep_renders()
    {
        ViewerTemplateResult result = await ResolveAsync(
            "{text.upper:{text.reverse:{text.lower:ABC}}}"
        );

        result.Text.Should().Be("CBA");
        result.Refusal.Should().Be(ViewerTemplateRefusal.None);
    }

    [Fact]
    public async Task Nesting_four_deep_is_refused_and_comes_back_as_written()
    {
        const string text = "{text.upper:{text.reverse:{text.lower:{text.trim: ABC }}}}";

        ViewerTemplateResult result = await ResolveAsync(text);

        result.Refusal.Should().Be(ViewerTemplateRefusal.TooDeep);
        result.Text.Should().Be(text);
    }

    [Fact]
    public async Task An_output_over_the_cap_is_refused_and_comes_back_as_written()
    {
        const string text = "{text.repeat:20|abcdefghij}";

        ViewerTemplateResult result = await ResolveAsync(
            text,
            ViewerTemplateLimits.WithOutputCap(100)
        );

        result.Refusal.Should().Be(ViewerTemplateRefusal.TooLong);
        result.Text.Should().Be(text);
    }

    [Fact]
    public async Task An_output_exactly_at_the_cap_is_kept()
    {
        ViewerTemplateResult result = await ResolveAsync(
            "{text.repeat:10|abcdefghij}",
            ViewerTemplateLimits.WithOutputCap(100)
        );

        result.Refusal.Should().Be(ViewerTemplateRefusal.None);
        result.Text.Should().HaveLength(100);
    }

    [Fact]
    public async Task The_broadcaster_surface_is_unchanged_a_repeat_there_is_not_clamped()
    {
        await using ServiceProvider services = BuildServices();
        TemplateResolver resolver = TextHelpersTableTests.BuildResolver(
            services.GetRequiredService<ITemplateHelperRegistry>()
        );

        string resolved = await resolver.ResolveAsync(
            "{text.repeat:30|ha}",
            new Dictionary<string, string>(ViewerSeeds, StringComparer.OrdinalIgnoreCase),
            broadcasterId: null
        );

        resolved.Should().HaveLength(60);
    }

    [Fact]
    public async Task The_viewer_row_holds_every_text_helper_and_the_named_read_only_info_and_nothing_that_writes()
    {
        await using ServiceProvider services = BuildServices();
        ITemplateHelperRegistry registry = services.GetRequiredService<ITemplateHelperRegistry>();

        List<string> keys =
        [
            .. registry.ForContext(TemplateHelperContext.Viewer).Select(entry => entry.Key),
        ];

        keys.Should()
            .Contain(["text.repeat", "text.upper", "user.name", "stream.title", "channel"]);
        keys.Where(key => key.StartsWith("text.", StringComparison.Ordinal))
            .Should()
            .BeEquivalentTo(
                registry
                    .ForContext(TemplateHelperContext.Command)
                    .Select(entry => entry.Key)
                    .Where(key => key.StartsWith("text.", StringComparison.Ordinal))
            );
        keys.Should()
            .NotContain(key =>
                key.StartsWith("include", StringComparison.Ordinal)
                || key.StartsWith("set", StringComparison.Ordinal)
                || key.StartsWith("count", StringComparison.Ordinal)
                || key.StartsWith("custom", StringComparison.Ordinal)
                || key.StartsWith("viewer.data", StringComparison.Ordinal)
            );
    }
}
