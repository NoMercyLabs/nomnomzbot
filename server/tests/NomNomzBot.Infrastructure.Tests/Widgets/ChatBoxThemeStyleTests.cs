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

using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using NomNomzBot.Infrastructure.Content.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// Pins the shipped chat_box.vue to the shared overlay theme: a message is a theme card (so print, dark and every
/// other theme draw it), its text reads the theme tokens (never a fixed color or the browser's link colors), the
/// avatar follows <c>showAvatar</c>, the list fills the browser source, and a line break in a message shows.
/// </summary>
public sealed class ChatBoxThemeStyleTests
{
    private static readonly string Asset = LoadChatBoxAsset();

    private static string LoadChatBoxAsset()
    {
        Assembly assembly = typeof(FirstPartyWidgetCatalogueSeeder).Assembly;
        using System.IO.Stream? stream = assembly.GetManifestResourceStream(
            "NomNomzBot.Infrastructure.Content.Widgets.Assets.chat_box.vue"
        );
        stream.Should().NotBeNull();
        using System.IO.StreamReader reader = new(stream!);
        return reader.ReadToEnd();
    }

    private static string Rule(string selector)
    {
        MatchCollection matches = Regex.Matches(
            Asset,
            @"(?m)^" + Regex.Escape(selector) + @"\s*\{([^}]*)\}"
        );
        matches.Should().NotBeEmpty($"the style rule '{selector}' must exist");
        return string.Join("\n", matches.Select(m => m.Groups[1].Value));
    }

    [Fact]
    public void A_message_line_is_a_theme_card_except_in_the_bare_text_theme()
    {
        Asset.Should().Contain("'nnz-card': cfg.theme !== 'transparent'");
    }

    [Fact]
    public void No_dark_or_light_rule_paints_the_line_flat_over_the_theme_surface()
    {
        Asset.Should().NotMatchRegex(@"\.theme-(dark|light) \.line,?\s*\{");
    }

    [Fact]
    public void The_avatar_is_drawn_only_when_show_avatar_is_on()
    {
        Asset.Should().Contain("v-if=\"cfg.showAvatar && l.avatarUrl\"");
        Asset
            .Should()
            .Contain("if (typeof s.showAvatar === 'boolean') cfg.showAvatar = s.showAvatar");
    }

    [Fact]
    public void The_list_fills_the_source_anchored_to_the_bottom_and_clips_older_lines()
    {
        string rule = Rule(".nnz-placed.nnz-chatbox");

        rule.Should().Contain("inset: 0");
        rule.Should().Contain("height: calc(100vh / var(--nnz-z))");
        rule.Should().Contain("justify-content: flex-end");
        rule.Should().Contain("overflow: hidden");
    }

    [Fact]
    public void A_line_break_in_a_message_is_drawn_as_a_line_break()
    {
        Rule(".body").Should().Contain("white-space: pre-line");
    }

    [Theory]
    [InlineData(".link-card", "color: var(--nnz-text)")]
    [InlineData(".link-card-title", "color: var(--nnz-text)")]
    [InlineData(".link-card-sub", "color: var(--nnz-muted)")]
    public void A_link_card_takes_its_text_colors_from_the_theme_tokens(
        string selector,
        string expected
    )
    {
        Rule(selector).Should().Contain(expected);
    }

    [Fact]
    public void A_link_card_and_a_plain_link_override_every_browser_link_state()
    {
        Asset.Should().Contain(".link-card:link,\n.link-card:visited,");
        Asset.Should().Contain(".link,\n.link:link,\n.link:visited,");
        Rule(".link-card").Should().Contain("text-decoration: none");
    }
}
