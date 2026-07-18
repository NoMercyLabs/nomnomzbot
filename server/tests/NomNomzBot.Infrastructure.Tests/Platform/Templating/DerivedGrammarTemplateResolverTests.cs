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
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// Proves the derived grammar template variables added for legacy-bot template parity:
/// {pastTense} (was/were by grammatical number, they-fallback "were"), {status} (live/offline from the
/// channel registry), {tense} (presentTense while live, pastTense once offline), {link}/{user.link}/
/// {target.link} (twitch.tv/&lt;login&gt;, bare form mirrors the target like every bare grammar var), the
/// value-carrying verb-agreement form {verb:sings|sing}, and {user.lastmessage}/{target.lastmessage}
/// (the viewer's most recent surviving non-command chat line — empty for a viewer with no history).
/// </summary>
public sealed class DerivedGrammarTemplateResolverTests
{
    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-9000-00000000c002");

    private readonly PronounGrammarTestDbContext _db;
    private readonly IChannelRegistry _registry;
    private readonly TemplateResolver _resolver;

    public DerivedGrammarTemplateResolverTests()
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
        Pronoun theyThem = new()
        {
            Name = "they/them",
            Subject = "they",
            Object = "them",
            Possessive = "their",
            GenderedTerm = "person",
            Singular = false,
        };
        _db.Pronouns.AddRange(heHim, theyThem);

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
                TwitchUserId = "444",
                Username = "dave",
                UsernameNormalized = "dave",
                DisplayName = "Dave",
                Pronoun = theyThem,
            },
            new User
            {
                TwitchUserId = "333",
                Username = "carol",
                UsernameNormalized = "carol",
                DisplayName = "Carol",
                // No pronoun on record — exercises the "were" fallback.
            }
        );
        _db.SaveChanges();

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(_db);
        ServiceProvider provider = services.BuildServiceProvider();

        _registry = Substitute.For<IChannelRegistry>();
        _resolver = new TemplateResolver(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _registry,
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        );
    }

    private static Dictionary<string, string> Seeds(string userId, string? target = null)
    {
        Dictionary<string, string> seeds = new(StringComparer.OrdinalIgnoreCase)
        {
            ["user.id"] = userId,
            ["user.name"] = userId switch
            {
                "111" => "alice",
                "444" => "dave",
                _ => "carol",
            },
        };
        if (target is not null)
            seeds["target"] = target;
        return seeds;
    }

    private void SetLive(bool isLive) =>
        _registry
            .Get(Channel)
            .Returns(
                new ChannelContext
                {
                    BroadcasterId = Channel,
                    TwitchChannelId = "39863651",
                    ChannelName = "stoney_eagle",
                    IsLive = isLive,
                }
            );

    // ── {pastTense} ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PastTense_WasForSingular_WereForPlural_WereWhenNoPronoun()
    {
        string singular = await _resolver.ResolveAsync("{pastTense}", Seeds("111"), Channel);
        string plural = await _resolver.ResolveAsync("{pastTense}", Seeds("444"), Channel);
        string fallback = await _resolver.ResolveAsync("{pastTense}", Seeds("333"), Channel);

        singular.Should().Be("was");
        plural.Should().Be("were");
        fallback.Should().Be("were");
    }

    // ── {status} + {tense} ───────────────────────────────────────────────────

    [Fact]
    public async Task Status_RendersLive_WhenChannelIsLive_AndOfflineOtherwise()
    {
        SetLive(true);
        (await _resolver.ResolveAsync("{status}", Seeds("111"), Channel)).Should().Be("live");

        SetLive(false);
        (await _resolver.ResolveAsync("{status}", Seeds("111"), Channel)).Should().Be("offline");
    }

    [Fact]
    public async Task Tense_PicksPresentTenseWhileLive_PastTenseOnceOffline()
    {
        SetLive(true);
        string live = await _resolver.ResolveAsync(
            "{user.tense}/{tense}",
            Seeds("111"), // he/him → is
            Channel
        );

        SetLive(false);
        string offline = await _resolver.ResolveAsync(
            "{user.tense}/{tense}",
            Seeds("111"),
            Channel
        );

        live.Should().Be("is/is");
        offline.Should().Be("was/was");
    }

    [Fact]
    public async Task Tense_BareForm_MirrorsTheTarget_WhenTargetPresent()
    {
        SetLive(false);
        string resolved = await _resolver.ResolveAsync(
            "{tense}",
            Seeds("111", target: "dave"), // caller singular, target they/them → were
            Channel
        );

        resolved.Should().Be("were");
    }

    [Fact]
    public async Task Tense_CapitalizedPlaceholder_CapitalizesTheResolvedValue()
    {
        SetLive(true);
        string resolved = await _resolver.ResolveAsync("{Tense} this real?", Seeds("111"), Channel);

        resolved.Should().Be("Is this real?");
    }

    // ── {link} family ────────────────────────────────────────────────────────

    [Fact]
    public async Task Link_RendersTwitchProfileUrl_BareMirrorsTargetWhenPresent()
    {
        string userOnly = await _resolver.ResolveAsync("{link}/{user.link}", Seeds("111"), Channel);
        string withTarget = await _resolver.ResolveAsync(
            "{link}/{target.link}",
            Seeds("111", target: "Dave"),
            Channel
        );

        userOnly.Should().Be("twitch.tv/alice/twitch.tv/alice");
        withTarget
            .Should()
            .Be("twitch.tv/dave/twitch.tv/dave", "the bare {link} mirrors the @mention target");
    }

    // ── {verb:singular|plural} ───────────────────────────────────────────────

    [Fact]
    public async Task VerbAgreement_PicksSingularForm_ForASingularSubject()
    {
        string resolved = await _resolver.ResolveAsync(
            "{subject} {verb:plays|play} games",
            Seeds("111"), // he
            Channel
        );

        resolved.Should().Be("he plays games");
    }

    [Fact]
    public async Task VerbAgreement_PicksPluralForm_ForTheyThem_AndForTheNoPronounFallback()
    {
        string plural = await _resolver.ResolveAsync(
            "{subject} {verb:plays|play}",
            Seeds("444"), // they
            Channel
        );
        string fallback = await _resolver.ResolveAsync(
            "{subject} {verb:plays|play}",
            Seeds("333"), // no pronoun → they
            Channel
        );

        plural.Should().Be("they play");
        fallback.Should().Be("they play");
    }

    [Fact]
    public async Task VerbAgreement_ExplicitSides_ResolveIndependently()
    {
        string resolved = await _resolver.ResolveAsync(
            "{user.verb:has|have} vs {target.verb:has|have}",
            Seeds("111", target: "dave"), // he vs they
            Channel
        );

        resolved.Should().Be("has vs have");
    }

    [Fact]
    public async Task VerbAgreement_MalformedPayload_LeavesTheRawToken()
    {
        string resolved = await _resolver.ResolveAsync("{verb:oops}", Seeds("111"), Channel);

        resolved
            .Should()
            .Be("{verb:oops}", "a payload without | is an authoring mistake to surface");
    }

    // ── {user.lastmessage} / {target.lastmessage} ────────────────────────────

    private void SeedChat(
        string id,
        string userId,
        string username,
        string message,
        bool isCommand,
        DateTime at
    )
    {
        _db.ChatMessages.Add(
            new ChatMessage
            {
                Id = id,
                BroadcasterId = Channel,
                UserId = userId,
                Username = username,
                DisplayName = username,
                UserType = "viewer",
                Message = message,
                IsCommand = isCommand,
                CreatedAt = at,
            }
        );
        _db.SaveChanges();
    }

    [Fact]
    public async Task LastMessage_ReturnsTheMostRecentNonCommandLine_PerSide()
    {
        SeedChat("m1", "111", "alice", "older line", false, new DateTime(2026, 7, 1));
        SeedChat("m2", "111", "alice", "latest alice line", false, new DateTime(2026, 7, 2));
        SeedChat("m3", "111", "alice", "!sr commands excluded", true, new DateTime(2026, 7, 3));
        SeedChat("m4", "444", "dave", "dave says hi", false, new DateTime(2026, 7, 2));

        string resolved = await _resolver.ResolveAsync(
            "[{user.lastmessage}] [{target.lastmessage}]",
            Seeds("111", target: "Dave"),
            Channel
        );

        resolved
            .Should()
            .Be(
                "[latest alice line] [dave says hi]",
                "commands are excluded and each side resolves its own viewer"
            );
    }

    [Fact]
    public async Task LastMessage_RendersEmpty_ForAViewerWithNoChatHistory()
    {
        string resolved = await _resolver.ResolveAsync(
            "[{user.lastmessage}]",
            Seeds("333"),
            Channel
        );

        resolved.Should().Be("[]", "a viewer with no history renders empty, never the raw token");
    }
}
