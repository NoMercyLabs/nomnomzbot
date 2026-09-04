// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;

namespace NomNomzBot.Domain.Music.ValueObjects;

/// <summary>
/// A short, speakable handle for one queued song request — four characters, e.g. <c>K7QM</c>.
///
/// <para>
/// It exists so a viewer can NAME the request they mean instead of the bot guessing. "Remove your latest"
/// is ambiguous the moment someone has two in the queue, and it is unusable for a moderator clearing
/// somebody else's. <c>!wrongsong K7QM</c> says exactly one thing.
/// </para>
///
/// <para>
/// The alphabet deliberately drops the characters that get misheard or mistyped when a code is read out on
/// stream: <c>O</c> and <c>0</c>, <c>I</c>/<c>L</c> and <c>1</c>, <c>S</c> and <c>5</c>, <c>B</c> and
/// <c>8</c>, <c>Z</c> and <c>2</c>. That leaves 23 symbols, so four characters give ~280,000 combinations
/// against a queue of a few dozen — collisions are handled by retrying rather than being assumed away.
/// </para>
/// </summary>
public static class SongCode
{
    /// <summary>Digits and uppercase letters minus every visually or audibly confusable pair.</summary>
    private const string Alphabet = "ACDEFGHJKMNPQRTUVWXY34679";

    /// <summary>How many characters a code has. Four is short enough to say and type, long enough to be safe.</summary>
    public const int Length = 4;

    /// <summary>
    /// A random code that is not in <paramref name="taken"/>.
    ///
    /// <para>
    /// Random rather than sequential on purpose: a counter would make the code predictable, and a viewer
    /// could guess a code that has not been issued yet and retract a request that is not theirs the moment
    /// it lands. It also avoids the code carrying accidental meaning ("mine was #3, so I was third").
    /// </para>
    ///
    /// <para>
    /// Gives up after a bounded number of attempts and returns null rather than looping forever — a queue
    /// large enough to exhaust the space is a different problem, and a hung request thread would be worse
    /// than a request without a code.
    /// </para>
    /// </summary>
    public static string? NextAvailable(ISet<string> taken, int maxAttempts = 32)
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            string candidate = Random();
            if (!taken.Contains(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>One random code, with no uniqueness guarantee.</summary>
    public static string Random()
    {
        Span<char> chars = stackalloc char[Length];
        for (int i = 0; i < Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];

        return new(chars);
    }

    /// <summary>
    /// Reads a code out of user input, or null when the input is not one.
    ///
    /// <para>
    /// Case-insensitive, because nobody types a code in caps. It does NOT repair confusable characters —
    /// accepting <c>0</c> as <c>O</c> would mean two different strings resolve to the same request, and the
    /// alphabet exists precisely so that never has to be guessed at.
    /// </para>
    /// </summary>
    public static string? TryParse(string? input)
    {
        string trimmed = (input ?? string.Empty).Trim().ToUpperInvariant();
        if (trimmed.Length != Length)
            return null;

        foreach (char c in trimmed)
        {
            if (!Alphabet.Contains(c, StringComparison.Ordinal))
                return null;
        }

        return trimmed;
    }
}
