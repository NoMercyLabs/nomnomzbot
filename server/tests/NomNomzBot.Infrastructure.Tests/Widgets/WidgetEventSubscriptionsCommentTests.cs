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
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Infrastructure.Widgets;

namespace NomNomzBot.Infrastructure.Tests.Widgets;

/// <summary>
/// A save takes event names from the widget's code, never from its comments. A help comment that shows
/// <c>NomNomz.on('cheer', …)</c> must not subscribe the widget to <c>cheer</c>.
/// </summary>
public sealed class WidgetEventSubscriptionsCommentTests
{
    private static readonly IReadOnlyCollection<string> None = [];

    [Theory]
    [InlineData("// NomNomz.on('cheer', f);")]
    [InlineData("var a = 1; // NomNomz.on('cheer', f);")]
    [InlineData("/* NomNomz.on('cheer', f); */")]
    [InlineData("/* line one\n   NomNomz.on('cheer', f);\n*/")]
    [InlineData("<!-- NomNomz.on('cheer', f); -->")]
    [InlineData("<!--\nNomNomz.on('cheer', f);\n-->")]
    public void A_call_inside_a_comment_adds_nothing(string bundle)
    {
        List<string> result = WidgetEventSubscriptions.Including(None, bundle);

        result.Should().BeEmpty();
    }

    [Fact]
    public void A_real_call_adds_its_name()
    {
        List<string> result = WidgetEventSubscriptions.Including(
            ["chat"],
            "NomNomz.on('follow', f);\nNomNomz.on(\"raid\", f);"
        );

        result.Should().Equal("chat", "follow", "raid");
    }

    [Fact]
    public void A_real_call_after_a_comment_still_adds_its_name()
    {
        List<string> result = WidgetEventSubscriptions.Including(
            None,
            "// NomNomz.on('cheer', f);\n/* NomNomz.on('sub', f); */\nNomNomz.on('raid', f);"
        );

        result.Should().Equal("raid");
    }

    [Fact]
    public void A_real_call_after_a_string_holding_slashes_still_adds_its_name()
    {
        List<string> result = WidgetEventSubscriptions.Including(
            None,
            "fetch('https://x'); NomNomz.on('raid', f)"
        );

        result.Should().Equal("raid");
    }

    [Fact]
    public void Slashes_inside_a_template_literal_do_not_start_a_comment()
    {
        List<string> result = WidgetEventSubscriptions.Including(
            None,
            "var u = `https://x/a`; NomNomz.on('raid', f)"
        );

        result.Should().Equal("raid");
    }

    [Fact]
    public void A_comment_marker_inside_a_double_quoted_string_does_not_start_a_comment()
    {
        List<string> result = WidgetEventSubscriptions.Including(
            None,
            "var s = \"/* not a comment\"; NomNomz.on('sub', f); var t = \"*/\";"
        );

        result.Should().Equal("sub");
    }

    [Fact]
    public void An_escaped_quote_does_not_end_a_string_early()
    {
        List<string> result = WidgetEventSubscriptions.Including(
            None,
            "var s = 'it\\'s // here'; NomNomz.on('raid', f)"
        );

        result.Should().Equal("raid");
    }

    [Fact]
    public void The_blank_template_subscribes_to_follow_only()
    {
        WidgetTemplate blank = WidgetTemplateCatalogue.All.Single(t => t.Key == "blank");

        List<string> result = WidgetEventSubscriptions.Including(None, blank.Source);

        result.Should().Equal("follow");
    }
}
