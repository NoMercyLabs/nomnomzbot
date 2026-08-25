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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Infrastructure.Platform.Templating;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// Proves each <see cref="TextTransformCatalog"/> primitive over real input: exact output for every
/// transform, non-ASCII (Dutch accented letters + an emoji surrogate pair) surviving casing/reversal
/// intact, truncate's cut boundary, and an unknown transform name failing honestly instead of silently
/// returning the input unchanged.
/// </summary>
public sealed class TextTransformCatalogTests
{
    [Theory]
    [InlineData("upper", "hello world", null, "HELLO WORLD")]
    [InlineData("lower", "HELLO World", null, "hello world")]
    [InlineData("title", "hello world", null, "Hello World")]
    [InlineData("spaced", "abc", null, "a b c")]
    [InlineData("reverse", "hello", null, "olleh")]
    [InlineData("trim", "  padded text  ", null, "padded text")]
    public void Transform_KnownName_ProducesExactOutput(
        string name,
        string input,
        string? argument,
        string expected
    )
    {
        Result<string> result = TextTransformCatalog.Apply(name, input, argument);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expected);
    }

    [Fact]
    public void Alternating_TogglesCaseAcrossLetters_SkippingWhitespace()
    {
        // Computed step by step: toggle starts upper=true, advances only on letter elements; the space
        // passes through untouched and does not consume a toggle.
        Result<string> result = TextTransformCatalog.Apply("alternating", "hello world", null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("HeLlO wOrLd");
    }

    [Fact]
    public void Upper_DutchAccentedLetters_CasesCorrectlyWithoutMangling()
    {
        Result<string> result = TextTransformCatalog.Apply("upper", "café ëèê", null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("CAFÉ ËÈÊ");
    }

    [Fact]
    public void Lower_DutchAccentedLetters_CasesCorrectlyWithoutMangling()
    {
        Result<string> result = TextTransformCatalog.Apply("lower", "CAFÉ ËÈÊ", null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("café ëèê");
    }

    [Fact]
    public void Reverse_DutchAccentedLetters_PreservesEachLetterIntact()
    {
        Result<string> result = TextTransformCatalog.Apply("reverse", "café", null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("éfac");
    }

    [Fact]
    public void Reverse_StringContainingEmoji_KeepsTheSurrogatePairTogether()
    {
        // U+1F600 (😀) is a surrogate pair — two UTF-16 chars. A naive char-by-char reverse would split the
        // pair and corrupt it into two lone unpaired surrogates. Reversing by text element must not.
        const string emoji = "\U0001F600";
        Result<string> result = TextTransformCatalog.Apply("reverse", $"a{emoji}b", null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be($"b{emoji}a");
        // Guard the intent explicitly: the emoji substring must still be a single, unsplit surrogate pair.
        result.Value.Should().Contain(emoji);
    }

    [Fact]
    public void Alternating_StringContainingEmoji_LeavesEmojiIntactAndUnmangled()
    {
        const string emoji = "\U0001F600";
        Result<string> result = TextTransformCatalog.Apply("alternating", $"café {emoji}", null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be($"CaFé {emoji}");
    }

    [Fact]
    public void Truncate_LongerThanLength_CutsAtTheBoundaryAndMarksTruncation()
    {
        Result<string> result = TextTransformCatalog.Apply("truncate", "abcdefghij", "5");

        result.IsSuccess.Should().BeTrue();
        // Cuts to exactly 5 text elements, then appends a single "…" to mark that it was cut.
        result.Value.Should().Be("abcde…");
    }

    [Fact]
    public void Truncate_ShorterThanLength_ReturnsInputUnchanged()
    {
        Result<string> result = TextTransformCatalog.Apply("truncate", "abc", "5");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("abc");
    }

    [Fact]
    public void Truncate_EmojiNearTheBoundary_CutsOnAGraphemeNotAHalfSurrogate()
    {
        const string emoji = "\U0001F600";
        Result<string> result = TextTransformCatalog.Apply("truncate", $"ab{emoji}cd", "3");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be($"ab{emoji}…");
    }

    [Fact]
    public void UnknownTransformName_FailsHonestly_RatherThanPassingInputThrough()
    {
        Result<string> result = TextTransformCatalog.Apply("frobnicate", "hello", null);

        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Contain("frobnicate");
        result.ErrorCode.Should().Be("TEMPLATE_UNKNOWN_TRANSFORM");
    }

    [Fact]
    public void Truncate_NonNumericArgument_FailsHonestly()
    {
        Result<string> result = TextTransformCatalog.Apply("truncate", "hello", "not-a-number");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("TEMPLATE_TRANSFORM_BAD_ARGUMENT");
    }
}
