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
using NomNomzBot.Infrastructure.Platform.Templating.Parsing;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Parsing;

/// <summary>
/// The template tokenizer (catalog section 3): one pass builds a tree of text and helper nodes. Each test
/// prints the tree in a fixed notation — <c>T(text)</c> for text, <c>H(key:[arg]|[arg])</c> for a helper —
/// and compares it exactly, so a wrong split, a lost escape or a wrong depth fails by name.
/// </summary>
public sealed class TemplateParserTests
{
    [Fact]
    public void A_helper_inside_an_argument_is_its_own_node_and_the_pipe_splits_arguments()
    {
        IReadOnlyList<TemplateNode> nodes = TemplateParser.Parse("Hi {text.upper:{user.name}|x} !");

        Dump(nodes).Should().Be("T(Hi )H(T(text.upper):[H(T(user.name))]|[T(x)])T( !)");
    }

    [Fact]
    public void A_pipe_inside_a_nested_helper_does_not_split_the_outer_arguments()
    {
        IReadOnlyList<TemplateNode> nodes = TemplateParser.Parse("{a:{verb:x|y}|z}");

        Dump(nodes).Should().Be("H(T(a):[H(T(verb):[T(x)]|[T(y)])]|[T(z)])");
    }

    [Fact]
    public void Escapes_print_the_character_and_never_split_or_open_a_helper()
    {
        IReadOnlyList<TemplateNode> nodes = TemplateParser.Parse(@"a\{b\}\|\:\\c {k:a\|b|c\:d} \n");

        Dump(nodes).Should().Be(@"T(a{b}|:\c )H(T(k):[T(a|b)]|[T(c:d)])T( \n)");
    }

    [Fact]
    public void An_unclosed_brace_is_text_and_a_later_helper_still_parses()
    {
        IReadOnlyList<TemplateNode> nodes = TemplateParser.Parse("{a {user}");

        Dump(nodes).Should().Be("T({a )H(T(user))");
    }

    [Fact]
    public void Empty_braces_and_a_stray_closing_brace_are_text()
    {
        IReadOnlyList<TemplateNode> nodes = TemplateParser.Parse("{} x }");

        Dump(nodes).Should().Be("T({} x })");
    }

    [Fact]
    public void Double_braces_parse_as_a_helper_whose_key_is_a_helper()
    {
        IReadOnlyList<TemplateNode> nodes = TemplateParser.Parse("{{user}}");

        Dump(nodes).Should().Be("H(H(T(user)))");
    }

    [Fact]
    public void The_ninth_nesting_level_stays_text_as_written()
    {
        IReadOnlyList<TemplateNode> nodes = TemplateParser.Parse(
            "{l1:{l2:{l3:{l4:{l5:{l6:{l7:{l8:{l9:x}}}}}}}}}"
        );

        Dump(nodes)
            .Should()
            .Be(
                "H(T(l1):[H(T(l2):[H(T(l3):[H(T(l4):[H(T(l5):[H(T(l6):[H(T(l7):[H(T(l8):[T({l9:x})])])])])])])])])"
            );
    }

    [Fact]
    public void Every_node_keeps_its_source_text_so_the_tree_writes_back_exactly()
    {
        const string template = @"a\{ {k:{x}|y\|z} {open";
        IReadOnlyList<TemplateNode> nodes = TemplateParser.Parse(template);

        string.Concat(nodes.Select(n => n.Raw)).Should().Be(template);
    }

    private static string Dump(IReadOnlyList<TemplateNode> nodes) =>
        string.Concat(nodes.Select(DumpOne));

    private static string DumpOne(TemplateNode node) =>
        node switch
        {
            TextNode text => $"T({text.Text})",
            PlaceholderNode helper => "H("
                + Dump(helper.Key)
                + (
                    helper.Arguments is null
                        ? string.Empty
                        : ":" + string.Join("|", helper.Arguments.Select(a => $"[{Dump(a)}]"))
                )
                + ")",
            _ => throw new InvalidOperationException(node.GetType().Name),
        };
}
