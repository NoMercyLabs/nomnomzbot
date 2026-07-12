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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// Proves the generic pronoun/gender grammar template variables: the five bare vars
/// (<c>{subject}/{object}/{possessive}/{presentTense}/{genderedTerm}</c>) mirror the @mention target's
/// pronoun when one is present in context and the triggering user's otherwise, the explicit
/// <c>{user.*}</c>/<c>{target.*}</c> forms always resolve their own side regardless of that mirroring, a
/// placeholder whose own first letter is uppercase capitalizes the resolved value, and a viewer with no
/// pronoun on record (or an unresolvable target) renders the universal they/them/their/person/are fallback
/// — so a grammar placeholder never renders raw.
/// </summary>
public sealed class PronounGrammarTemplateResolverTests
{
    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-9000-00000000c001");

    private readonly PronounGrammarTestDbContext _db;
    private readonly TemplateResolver _resolver;

    public PronounGrammarTemplateResolverTests()
    {
        _db = PronounGrammarTestDbContext.New();

        Pronoun heHim = new()
        {
            Name = "he/him",
            Subject = "he",
            Object = "him",
            Possessive = "his",
            GenderedTerm = "guy",
            Singular = true,
        };
        Pronoun sheHer = new()
        {
            Name = "she/her",
            Subject = "she",
            Object = "her",
            Possessive = "her",
            GenderedTerm = "gal",
            Singular = true,
        };
        Pronoun theyThem = new()
        {
            Name = "they/them",
            Subject = "they",
            Object = "them",
            Possessive = "their",
            GenderedTerm = "person",
            Singular = false,
        };
        _db.Pronouns.AddRange(heHim, sheHer, theyThem);

        _db.Users.AddRange(
            new User
            {
                TwitchUserId = "111",
                Username = "alice",
                UsernameNormalized = "alice",
                DisplayName = "Alice",
                Pronoun = heHim,
            },
            new User
            {
                TwitchUserId = "222",
                Username = "bob",
                UsernameNormalized = "bob",
                DisplayName = "Bob",
                Pronoun = sheHer,
            },
            new User
            {
                TwitchUserId = "333",
                Username = "carol",
                UsernameNormalized = "carol",
                DisplayName = "Carol",
                // No pronoun on record — exercises the they/them/their/person/are fallback.
            },
            new User
            {
                TwitchUserId = "444",
                Username = "dave",
                UsernameNormalized = "dave",
                DisplayName = "Dave",
                Pronoun = theyThem,
            }
        );
        _db.SaveChanges();

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(_db);
        ServiceProvider provider = services.BuildServiceProvider();

        _resolver = new TemplateResolver(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IChannelRegistry>(),
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        );
    }

    private static Dictionary<string, string> Seeds(string userId, string? target = null)
    {
        Dictionary<string, string> seeds = new(StringComparer.OrdinalIgnoreCase)
        {
            ["user.id"] = userId,
        };
        if (target is not null)
            seeds["target"] = target;
        return seeds;
    }

    // ── Bare vars: caller vs. target ─────────────────────────────────────────

    [Fact]
    public async Task Bare_ResolvesFromCaller_WhenNoTargetPresent()
    {
        string resolved = await _resolver.ResolveAsync(
            "{subject}/{object}/{possessive}/{genderedTerm}/{presentTense}",
            Seeds("111"), // alice = he/him
            Channel
        );

        resolved.Should().Be("he/him/his/guy/is");
    }

    [Fact]
    public async Task Bare_ResolvesFromTarget_WhenTargetPresentInContext()
    {
        string resolved = await _resolver.ResolveAsync(
            "{subject}/{object}/{possessive}/{genderedTerm}/{presentTense}",
            Seeds("111", target: "bob"), // caller = he/him, target = she/her
            Channel
        );

        resolved
            .Should()
            .Be("she/her/her/gal/is", "the bare vars mirror the target, not the caller");
    }

    [Fact]
    public async Task Bare_FallsBackToTheyThem_WhenTargetPresentButUnresolvable()
    {
        string resolved = await _resolver.ResolveAsync(
            "{subject}",
            Seeds("111", target: "nonexistent"), // caller = he/him, target doesn't exist
            Channel
        );

        resolved
            .Should()
            .Be(
                "they",
                "an unresolvable target falls back rather than silently using the caller's"
            );
    }

    // ── Explicit user./target. forms resolve independently ──────────────────

    [Fact]
    public async Task Explicit_UserAndTargetForms_ResolveIndependently_RegardlessOfBareMirroring()
    {
        string resolved = await _resolver.ResolveAsync(
            "{user.subject} vs {target.subject}",
            Seeds("111", target: "bob"),
            Channel
        );

        resolved.Should().Be("he vs she");
    }

    [Fact]
    public async Task Fallback_AppliesToExplicitTargetForm_WhenTargetUnresolvable()
    {
        string resolved = await _resolver.ResolveAsync(
            "{target.subject}",
            Seeds("111", target: "nonexistent"),
            Channel
        );

        resolved.Should().Be("they");
    }

    // ── presentTense agreement with Pronoun.Singular ─────────────────────────

    [Fact]
    public async Task PresentTense_IsForASingularPronoun_AreForAPluralPronoun()
    {
        string singular = await _resolver.ResolveAsync("{presentTense}", Seeds("111"), Channel); // he/him
        string plural = await _resolver.ResolveAsync("{presentTense}", Seeds("444"), Channel); // they/them (real row)

        singular.Should().Be("is");
        plural.Should().Be("are");
    }

    // ── Fallback for a viewer with no pronoun on record ──────────────────────

    [Fact]
    public async Task Fallback_TheyThemTheirPersonAre_WhenCallerHasNoPronounOnRecord()
    {
        string resolved = await _resolver.ResolveAsync(
            "{subject}/{object}/{possessive}/{genderedTerm}/{presentTense}",
            Seeds("333"), // carol — no Pronoun row
            Channel
        );

        resolved.Should().Be("they/them/their/person/are");
    }

    // ── Capitalization keys off the placeholder's own first letter ──────────

    [Fact]
    public async Task Capitalized_BareVariant_UppercasesFirstLetterOfResolvedValue()
    {
        string resolved = await _resolver.ResolveAsync("{Subject} smiled.", Seeds("111"), Channel);

        resolved.Should().Be("He smiled.");
    }

    [Fact]
    public async Task Capitalized_ExplicitUserAndTargetVariants_UppercaseFirstLetter()
    {
        string resolved = await _resolver.ResolveAsync(
            "{User.Possessive} idea; {Target.GenderedTerm} waved.",
            Seeds("111", target: "bob"),
            Channel
        );

        resolved.Should().Be("His idea; Gal waved.");
    }

    [Fact]
    public async Task LowercaseExplicitVariant_DoesNotUppercase_EvenWithACapitalizedSecondSegment()
    {
        // Only the placeholder's OWN first letter counts ("u" is lowercase here) — the capitalized
        // "Subject" segment after the dot does not, on its own, trigger capitalization.
        string resolved = await _resolver.ResolveAsync("{user.Subject}", Seeds("111"), Channel);

        resolved.Should().Be("he");
    }

    // ── Caller seeds still win over auto-resolution ──────────────────────────

    [Fact]
    public async Task CallerSeed_TakesPrecedenceOverAutoResolvedPronoun()
    {
        Dictionary<string, string> seeds = Seeds("111");
        seeds["subject"] = "OVERRIDDEN";

        string resolved = await _resolver.ResolveAsync("{subject}", seeds, Channel);

        resolved.Should().Be("OVERRIDDEN");
    }
}
