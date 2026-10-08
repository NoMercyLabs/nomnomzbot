// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;

namespace NomNomzBot.Infrastructure.Content.Moderation;

/// <summary>
/// The curated spam seed corpus (spam-defense.md §4.1): phrase skeletons already normalized by the L0
/// normalizer, and malicious domains. Read from the embedded <c>spam-seed-corpus.txt</c>, whose lines are
/// <c>skeleton:&lt;value&gt;</c> or <c>domain:&lt;value&gt;</c>; <c>#</c> lines and blank lines are comments.
/// </summary>
public sealed record SpamSeedCorpus(IReadOnlyList<string> Skeletons, IReadOnlyList<string> Domains)
{
    private const string ResourceName =
        "NomNomzBot.Infrastructure.Moderation.Assets.spam-seed-corpus.txt";
    private const string SkeletonPrefix = "skeleton:";
    private const string DomainPrefix = "domain:";

    /// <summary>Read the embedded corpus. Fails loudly if the resource is missing or a line is malformed.</summary>
    public static SpamSeedCorpus Load()
    {
        Assembly assembly = typeof(SpamSeedCorpus).Assembly;
        // Fully qualified: NomNomzBot.Domain.Stream shadows System.IO.Stream in this namespace.
        using System.IO.Stream? stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
            throw new InvalidOperationException(
                $"Embedded spam seed corpus '{ResourceName}' was not found in {assembly.GetName().Name}."
            );

        using StreamReader reader = new(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Parse corpus text. Duplicate values within one kind collapse to a single entry.</summary>
    public static SpamSeedCorpus Parse(string text)
    {
        List<string> skeletons = [];
        List<string> domains = [];

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.StartsWith(SkeletonPrefix, StringComparison.Ordinal))
                AddOnce(skeletons, line[SkeletonPrefix.Length..]);
            else if (line.StartsWith(DomainPrefix, StringComparison.Ordinal))
                AddOnce(domains, line[DomainPrefix.Length..]);
            else
                throw new FormatException($"Unrecognised spam seed corpus line: '{line}'.");
        }

        return new SpamSeedCorpus(skeletons, domains);
    }

    private static void AddOnce(List<string> target, string value)
    {
        if (value.Length > 0 && !target.Contains(value))
            target.Add(value);
    }
}
