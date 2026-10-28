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
using NomNomzBot.Application.Commands.Builtin;

namespace NomNomzBot.Application.Tests.Commands;

/// <summary>
/// Proves <see cref="MentionParser.ParseUserMention"/> — the single shared implementation now used by
/// every builtin/pipeline-action call site that used to strip a leading '@' inline (S069b) — parses the
/// three shapes those call sites actually feed it.
/// </summary>
public sealed class MentionParserTests
{
    [Fact]
    public void An_at_prefixed_mention_loses_the_at_sign()
    {
        MentionParser.ParseUserMention("@stoney_eagle").Should().Be("stoney_eagle");
    }

    [Fact]
    public void A_bare_name_with_no_at_sign_passes_through_unchanged()
    {
        MentionParser.ParseUserMention("stoney_eagle").Should().Be("stoney_eagle");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@")]
    public void An_invalid_or_empty_mention_parses_to_an_empty_string(string? raw)
    {
        MentionParser.ParseUserMention(raw).Should().BeEmpty();
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed_before_the_at_sign_is_stripped()
    {
        MentionParser.ParseUserMention("  @stoney_eagle  ").Should().Be("stoney_eagle");
    }
}
