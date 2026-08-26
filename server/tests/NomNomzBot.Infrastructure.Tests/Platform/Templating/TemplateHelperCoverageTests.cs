// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// S042's drift guard: <see cref="TemplateHelperRegistry"/> must never diverge from what
/// <c>TemplateResolver</c> actually resolves. Both sides are enumerated STRUCTURALLY from the real
/// <c>TemplateResolver.cs</c> source text — never a hand-written expectation list — so a helper that
/// becomes resolvable without being registered, or registered without being resolvable, fails here by
/// name.
///
/// Extraction targets the exact code shapes the resolver uses to read/write its variable bag
/// (<c>vars.TryAdd("key"</c>, <c>vars["key"]</c>, <c>vars.GetValueOrDefault("key"</c>,
/// <c>NeedsAny(needed, "key", ...)</c>, <c>needed.Contains("key"</c>) — every literal placeholder key
/// the resolver touches is written in one of these forms. Class/method names never match because they
/// start uppercase; a lowercase-first-letter filter removes them without a hand-picked denylist for
/// that class of noise. What remains after that filter is either a real placeholder key/prefix stem or
/// one of a small, explicitly justified set of non-key string VALUES the resolver happens to assign
/// (booleans, fallback words) — <see cref="NonKeyValueLiterals"/> documents exactly why each is there.
/// </summary>
public sealed partial class TemplateHelperCoverageTests
{
    // Fallback/constant VALUES the resolver assigns into its variable bag or compares against — never
    // placeholder KEYS — that happen to share the same lowercase-identifier shape as a real key.
    private static readonly HashSet<string> NonKeyValueLiterals = new(StringComparer.Ordinal)
    {
        "a",
        "b",
        "c", // {random.pick.a.b.c} doc-comment example values
        "are",
        "were",
        "was",
        "is", // grammar tense fallback/comparison values
        "true",
        "false", // {stream.isLive} value strings
        "live",
        "offline", // {status} value strings
        "person",
        "they",
        "them",
        "their", // universal pronoun-fallback VALUES (not keys)
        "unknown",
        "someone", // fallback display VALUES
        "twitch_bot", // bot-name fallback source tag, not a placeholder
        "s", // grapheme/pluralization filler, not a placeholder
        "pasttense",
        "user.pasttense",
        "target.pasttense", // internal tense-fallback lookup key,
        // never a placeholder a streamer types directly ({presentTense}/{tense} are — both registered)
    };

    // Internal routing stems the resolver uses to detect a SIDE (user vs target vs viewer) before
    // dispatching to the concrete keys already covered by literal registry entries below — not
    // themselves placeholders a streamer would type.
    private static readonly HashSet<string> InternalRoutingStems = new(StringComparer.Ordinal)
    {
        "user.",
        "target.",
        "viewer.",
    };

    // Supplied by the CALLER's seedVariables (PipelineEngine / EventResponseExecutor / command
    // dispatch) and never assigned inside TemplateResolver.cs itself — resolved purely by the generic
    // "already in the merged vars dict" substitution pass, so they never appear as a literal
    // TryAdd/GetValueOrDefault/NeedsAny call site there.
    private static readonly HashSet<string> SeedPassthroughOnly = new(StringComparer.Ordinal)
    {
        "args.",
        "user",
        "user.id",
        "user.name",
        "user.provider",
        "target",
        "provider", // set by the provider-scoped event handlers (S022b) into EventResponseExecutor's
        // seed variables (e.g. NewSubscriptionEventHandler) — never assigned inside TemplateResolver.cs
        "broadcaster",
        "channel.name",
        "channel.title",
        "channel.game",
        "title",
        "game",
        "raw.message", // Discord-only seed aliases — supplied by DiscordGoLiveNotificationHandler /
        // SendDiscordNotificationAction, never assigned inside TemplateResolver.cs
    };

    // Resolved via a colon-containing literal (verb:.../user.verb:.../target.verb:...) or a dedicated
    // pre-pass regex (list.pick./custom./transform.) that this test's [a-zA-Z0-9_.]-only extractor
    // cannot capture — verified directly against TemplateResolver.cs by file/line in the comment on each
    // usage, not by the structural scan.
    private static readonly HashSet<string> ColonOrPrePassOnly = new(StringComparer.Ordinal)
    {
        "verb:", // ResolveVerbAgreement: key.StartsWith("verb:", ...)
        "user.verb:", // ResolveVerbAgreement: key.StartsWith("user.verb:", ...)
        "target.verb:", // ResolveVerbAgreement: key.StartsWith("target.verb:", ...)
        "list.pick.", // ExpandListPicksAsync pre-pass: template.Contains("{list.pick.", ...) + its own regex
        "custom.", // ExpandCustomDataAsync pre-pass: template.Contains("{custom.", ...) + its own regex
        "transform.", // ApplyTransforms: its own TransformPattern regex, run after main substitution
    };

    [GeneratedRegex(@"vars\.TryAdd\(""([a-zA-Z][a-zA-Z0-9_.]*)""")]
    private static partial Regex TryAddPattern();

    [GeneratedRegex(@"vars\[""([a-zA-Z][a-zA-Z0-9_.]*)""\]")]
    private static partial Regex IndexerPattern();

    [GeneratedRegex(@"vars\.GetValueOrDefault\(""([a-zA-Z][a-zA-Z0-9_.]*)""")]
    private static partial Regex GetValueOrDefaultPattern();

    [GeneratedRegex(@"NeedsAny\(\s*needed,([^)]*)\)")]
    private static partial Regex NeedsAnyCallPattern();

    [GeneratedRegex(@"needed\.Contains\(""([a-zA-Z][a-zA-Z0-9_.]*)""")]
    private static partial Regex NeededContainsPattern();

    // Prefix families dispatched via a StartsWith predicate inside a `needed.Where(n => ...)` /
    // `needed.Any(n => ...)` lambda (count.*, viewer.data.*, custom.*, random.number.*, ...) rather than
    // a plain literal comparison.
    [GeneratedRegex(@"StartsWith\(""([a-zA-Z][a-zA-Z0-9_.]*)""")]
    private static partial Regex StartsWithPattern();

    // Single-key equality checks against a lambda-bound key variable (e.g. random.user).
    [GeneratedRegex(@"key\.Equals\(""([a-zA-Z][a-zA-Z0-9_.]*)""")]
    private static partial Regex KeyEqualsPattern();

    [GeneratedRegex(@"""([a-zA-Z][a-zA-Z0-9_.]*)""")]
    private static partial Regex QuotedLiteralPattern();

    [GeneratedRegex(@"PronounGrammarSuffixes\s*=\s*\[(.*?)\];", RegexOptions.Singleline)]
    private static partial Regex PronounGrammarSuffixesArrayPattern();

    [Fact]
    public void Every_helper_the_resolver_can_resolve_is_registered_for_at_least_one_context()
    {
        HashSet<string> resolverLiterals = ExtractResolverLiterals();

        List<string> unregistered =
        [
            .. resolverLiterals.Where(literal =>
                !TemplateHelperRegistry.All.Any(entry => EntryCoversLiteral(entry, literal))
            ),
        ];

        unregistered
            .Should()
            .BeEmpty(
                "TemplateResolver.cs resolves these keys/prefixes but TemplateHelperRegistry has no "
                    + $"matching entry: {string.Join(", ", unregistered)}"
            );
    }

    [Fact]
    public void Every_registered_helper_is_actually_resolvable_by_the_resolver()
    {
        HashSet<string> resolverLiterals = ExtractResolverLiterals();

        List<string> unresolvable =
        [
            .. TemplateHelperRegistry
                .All.Where(entry => !SeedPassthroughOnly.Contains(entry.Prefix ?? entry.Key))
                .Where(entry => !ColonOrPrePassOnly.Contains(entry.Prefix ?? entry.Key))
                .Where(entry =>
                    !resolverLiterals.Any(literal => EntryCoversLiteral(entry, literal))
                )
                .Select(entry => entry.Key),
        ];

        unresolvable
            .Should()
            .BeEmpty(
                "TemplateHelperRegistry registers these keys but TemplateResolver.cs never resolves "
                    + $"them (and they are not documented seed-passthrough): {string.Join(", ", unresolvable)}"
            );
    }

    private static bool EntryCoversLiteral(TemplateHelperEntry entry, string literal)
    {
        if (entry.Prefix is not null)
            return string.Equals(entry.Prefix, literal, StringComparison.Ordinal)
                || literal.StartsWith(entry.Prefix, StringComparison.Ordinal)
                || entry.Prefix.StartsWith(literal, StringComparison.Ordinal);

        return string.Equals(entry.Key, literal, StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> ExtractResolverLiterals()
    {
        string source = File.ReadAllText(ResolveResolverSourcePath());
        HashSet<string> literals = new(StringComparer.Ordinal);

        foreach (Match m in TryAddPattern().Matches(source))
            literals.Add(m.Groups[1].Value);
        foreach (Match m in IndexerPattern().Matches(source))
            literals.Add(m.Groups[1].Value);
        foreach (Match m in GetValueOrDefaultPattern().Matches(source))
            literals.Add(m.Groups[1].Value);
        foreach (Match m in NeededContainsPattern().Matches(source))
            literals.Add(m.Groups[1].Value);
        foreach (Match m in StartsWithPattern().Matches(source))
            literals.Add(m.Groups[1].Value);
        foreach (Match m in KeyEqualsPattern().Matches(source))
            literals.Add(m.Groups[1].Value);
        foreach (Match call in NeedsAnyCallPattern().Matches(source))
        foreach (Match arg in QuotedLiteralPattern().Matches(call.Groups[1].Value))
            literals.Add(arg.Groups[1].Value);

        // The pronoun-grammar suffixes (subject/object/possessive/presentTense/genderedTerm) and the
        // live/offline tense key are built by string INTERPOLATION ($"{side}{suffix}", $"{side}tense")
        // off the PronounGrammarSuffixes array and the bare/user./target. sides — not something a plain
        // literal regex can match — but the array itself is real, non-hand-typed source content: pull
        // its entries structurally and expand the three known sides (bare, user., target.) the same way
        // ApplyPronounGrammarBareAndFallback/ResolveVerbAgreement do.
        Match suffixArray = PronounGrammarSuffixesArrayPattern().Match(source);
        if (suffixArray.Success)
        {
            foreach (Match m in QuotedLiteralPattern().Matches(suffixArray.Groups[1].Value))
            {
                string suffix = m.Groups[1].Value;
                literals.Add(suffix);
                literals.Add($"user.{suffix}");
                literals.Add($"target.{suffix}");
            }
        }
        literals.Add("tense");
        literals.Add("user.tense");
        literals.Add("target.tense");

        literals.ExceptWith(NonKeyValueLiterals);
        literals.ExceptWith(InternalRoutingStems);

        return literals;
    }

    private static string ResolveResolverSourcePath()
    {
        const string relative =
            "server/src/NomNomzBot.Infrastructure/Platform/Templating/TemplateResolver.cs";
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(
            $"Could not locate '{relative}' above '{AppContext.BaseDirectory}'."
        );
    }
}
