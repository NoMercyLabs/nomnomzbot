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
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat.Adapters;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the two structural decoration steps (chat-decoration spec §0/§9·5): <see cref="ExplodeTextAdapter"/> tiles a
/// text fragment into whitespace/word tokens with no loss, and <see cref="ImplodeTextAdapter"/> collapses the text runs
/// back — so the chain can match whole words in the middle yet a message with no matches comes out exactly as it went in.
/// </summary>
public sealed class TextFragmentAdapterTests
{
    private static readonly ExplodeTextAdapter Explode = new();
    private static readonly ImplodeTextAdapter Implode = new();

    private static ChatDecorationContext Context(params ChatMessageFragment[] fragments) =>
        new() { Fragments = [.. fragments] };

    private static ChatMessageFragment Text(string text) => new() { Type = "text", Text = text };

    private static IEnumerable<(string Type, string Text)> Shape(ChatDecorationContext context) =>
        context.Fragments.Select(fragment => (fragment.Type, fragment.Text));

    // ─── ExplodeTextAdapter (step 10) ─────────────────────────────────────────

    [Fact]
    public async Task Explode_splits_text_into_alternating_whitespace_and_word_tokens()
    {
        ChatDecorationContext context = Context(Text("hello  world Kappa"));

        await Explode.DecorateAsync(context);

        Shape(context)
            .Should()
            .Equal(
                ("text", "hello"),
                ("text", "  "), // the double space survives as a single whitespace token
                ("text", "world"),
                ("text", " "),
                ("text", "Kappa")
            );
    }

    [Fact]
    public async Task Explode_passes_non_text_fragments_through_and_splits_only_text()
    {
        ChatMessageFragment kappa = new()
        {
            Type = "emote",
            Text = "Kappa",
            EmoteId = "25",
        };
        ChatDecorationContext context = Context(Text("hey "), kappa, Text(" bye"));

        await Explode.DecorateAsync(context);

        Shape(context)
            .Should()
            .Equal(
                ("text", "hey"),
                ("text", " "),
                ("emote", "Kappa"),
                ("text", " "),
                ("text", "bye")
            );
        context.Fragments[2].EmoteId.Should().Be("25"); // the emote fragment is carried through untouched
    }

    [Fact]
    public void Explode_does_not_apply_when_there_is_no_non_empty_text()
    {
        ChatDecorationContext context = Context(
            new ChatMessageFragment { Type = "emote", Text = "Kappa" },
            Text("")
        );

        Explode.AppliesTo(context).Should().BeFalse();
    }

    // ─── ImplodeTextAdapter (step 80) ─────────────────────────────────────────

    [Fact]
    public async Task Implode_merges_adjacent_text_runs_and_leaves_non_text_standalone()
    {
        ChatMessageFragment kappa = new()
        {
            Type = "emote",
            Text = "Kappa",
            EmoteId = "25",
        };
        ChatDecorationContext context = Context(
            Text("hey"),
            Text(" "),
            kappa,
            Text(" "),
            Text("bye")
        );

        await Implode.DecorateAsync(context);

        Shape(context).Should().Equal(("text", "hey "), ("emote", "Kappa"), ("text", " bye"));
    }

    [Fact]
    public void Implode_does_not_apply_to_a_single_fragment()
    {
        Implode.AppliesTo(Context(Text("solo"))).Should().BeFalse();
    }

    // ─── Round trip ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Explode_then_implode_reconstructs_the_original_fragments()
    {
        ChatMessageFragment kappa = new()
        {
            Type = "emote",
            Text = "Kappa",
            EmoteId = "25",
        };
        ChatDecorationContext context = Context(Text("hello  world "), kappa, Text(" gg"));

        await Explode.DecorateAsync(context);
        await Implode.DecorateAsync(context);

        Shape(context)
            .Should()
            .Equal(("text", "hello  world "), ("emote", "Kappa"), ("text", " gg"));
    }
}
