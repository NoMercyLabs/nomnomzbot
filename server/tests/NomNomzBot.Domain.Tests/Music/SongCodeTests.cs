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
using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Domain.Tests.Music;

/// <summary>
/// The short handle a viewer types to name one song request. It gets read aloud on stream and typed back
/// into chat by someone who was half-listening, so the properties that matter are: no confusable
/// characters, no collision inside a queue, and no accepting a near-miss as if it were exact.
/// </summary>
public sealed class SongCodeTests
{
    [Fact]
    public void A_code_never_contains_a_character_that_gets_misheard_or_mistyped()
    {
        // 0/O, 1/I/L, 5/S, 8/B, 2/Z are the pairs that ruin a code read out over a stream. Sampled widely
        // rather than once: a single draw would pass by luck even if the alphabet still held them.
        const string banned = "OIL01SZB258";

        for (int i = 0; i < 2_000; i++)
        {
            string code = SongCode.Random();

            code.Should().HaveLength(SongCode.Length);
            code.Should().NotContainAny([.. banned.Select(c => c.ToString())]);
        }
    }

    [Fact]
    public void A_new_code_avoids_the_ones_already_in_use()
    {
        // Exhaust nothing — just prove the taken set is actually consulted.
        HashSet<string> taken = new(StringComparer.Ordinal);
        for (int i = 0; i < 200; i++)
        {
            string? code = SongCode.NextAvailable(taken);
            code.Should().NotBeNull();
            taken.Add(code);
        }

        taken.Should().HaveCount(200, "every issued code must be distinct within a queue");
    }

    [Fact]
    public void Giving_up_returns_null_rather_than_looping_forever()
    {
        // A queue big enough to exhaust the space is a different problem; hanging the request thread while
        // it retries would be a worse one.
        // The ACTUAL whole space, not a large sample of it. 50,000 random draws cover only about an eighth
        // of it, so a bounded search still finds a free code and the test passes for the wrong reason.
        const string alphabet = "ACDEFGHJKMNPQRTUVWXY34679";
        HashSet<string> everything = new(StringComparer.Ordinal);
        foreach (char a in alphabet)
        foreach (char b in alphabet)
        foreach (char c in alphabet)
        foreach (char d in alphabet)
            everything.Add(new([a, b, c, d]));

        everything
            .Should()
            .HaveCount(alphabet.Length * alphabet.Length * alphabet.Length * alphabet.Length);
        SongCode.NextAvailable(everything).Should().BeNull();
    }

    [Theory]
    [InlineData("K7QM", "K7QM")]
    [InlineData("k7qm", "K7QM")] // nobody types a code in caps
    [InlineData("  K7QM  ", "K7QM")]
    public void A_typed_code_is_read_back_in_its_canonical_form(string input, string expected) =>
        SongCode.TryParse(input).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("K7Q")] // too short
    [InlineData("K7QMX")] // too long
    [InlineData("K7Q0")] // 0 is not in the alphabet
    [InlineData("K7Q!")]
    [InlineData("some song name")]
    public void Anything_that_is_not_a_code_is_refused_rather_than_repaired(string? input)
    {
        // Refusing matters more than parsing: "!wrongsong never gonna give you up" must NOT be read as a
        // code, and a near-miss like K7Q0 must not be silently corrected to K7QO — two different strings
        // resolving to one request is how somebody retracts a song they did not mean to.
        SongCode.TryParse(input).Should().BeNull();
    }
}
