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
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Application.Tests.Templating;

/// <summary>
/// The bug: a template carried over as <c>${user}</c> rendered as <c>$Astro</c> on the owner's live
/// stream, because the resolver substitutes <c>{user}</c> and leaves the preceding <c>$</c> alone.
/// The fix rewrites the placeholder rather than teaching the resolver to eat a <c>$</c> — which would
/// have destroyed an intentional literal dollar in an economy template.
/// </summary>
public class TemplateSyntaxNormalizerTests
{
    [Fact]
    public void A_dollar_placeholder_for_a_known_variable_loses_the_dollar()
    {
        TemplateSyntaxNormalizer
            .Normalize("${user} is now lurking. Enjoy the stream!")
            .Should()
            .Be("{user} is now lurking. Enjoy the stream!");
    }

    [Fact]
    public void Every_dollar_placeholder_in_one_template_is_rewritten()
    {
        TemplateSyntaxNormalizer
            .Normalize("${user} raided ${channel.name}")
            .Should()
            .Be("{user} raided {channel.name}");
    }

    [Fact]
    public void A_dollar_before_an_UNKNOWN_name_keeps_its_dollar()
    {
        // The guard that stops this fix becoming a worse bug. This bot has an economy; a template
        // meaning "$" followed by an amount must survive untouched.
        const string Currency = "You have ${totallyNotAVariable} in the bank";
        TemplateSyntaxNormalizer.Normalize(Currency).Should().Be(Currency);
    }

    [Fact]
    public void A_plain_placeholder_is_left_exactly_as_it_is()
    {
        const string Already = "{user} is now lurking";
        TemplateSyntaxNormalizer.Normalize(Already).Should().Be(Already);
    }

    [Fact]
    public void A_literal_dollar_not_followed_by_braces_is_untouched()
    {
        const string Price = "that costs $5, {user}";
        TemplateSyntaxNormalizer.Normalize(Price).Should().Be(Price);
    }

    [Fact]
    public void A_javascript_template_literal_WOULD_be_rewritten_which_is_why_code_never_reaches_here()
    {
        // Documents a real sharp edge rather than pretending it away. `count` IS a registered helper
        // (TemplateHelperRegistry), so `${count}` in Vue source would be rewritten to `{count}` and the
        // code would break. This normalizer is not code-aware and should not try to be — a heuristic
        // guessing "is this JS?" would be wrong in both directions.
        //
        // The actual protection is that code-bearing columns are never [TemplatedUserContent], so they
        // never reach this method. WidgetGalleryItem.SourceCode is the one that matters and
        // TemplateSyntaxInterceptorTests pins it as unmarked. Verified against the owner's real
        // database, where the only two rows containing "${" are exactly that column.
        TemplateSyntaxNormalizer
            .Normalize("`${count} viewers watching`")
            .Should()
            .Be("`{count} viewers watching`");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Null_and_empty_pass_through(string? input) =>
        TemplateSyntaxNormalizer.Normalize(input).Should().Be(input);

    [Fact]
    public void The_known_set_comes_from_the_real_registry_not_a_hand_written_list()
    {
        // If a helper is added to or removed from the registry, this normalizer follows automatically.
        // Asserting against the registry itself is what makes that true rather than hoped-for.
        TemplateHelperEntry first = TemplateHelperRegistry.All[0];
        string key = first.Prefix is not null ? first.Prefix + "anything" : first.Key;

        TemplateSyntaxNormalizer.Normalize("${" + key + "}").Should().Be("{" + key + "}");
    }
}
