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
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating;

/// <summary>
/// Proves a shoutout line renders whole through the real resolver: the target's game, title and live state
/// (not the broadcaster's) fill {game}/{title}/{status}/{tense}, subject and verb agree for every pronoun
/// set, and no placeholder is left raw in the owner's template, the tone catalogue or the old bot's lines.
/// </summary>
public sealed class ShoutoutLineRenderTests
{
    private static readonly Guid Channel = Guid.Parse("0192b400-0000-7000-9000-00000000c0a2");
    private const string OwnerTemplate =
        "Check out @{name}! {subject} {tense} streaming {game}. Go give {object} a follow!";

    // ShoutoutQueueService.cs:29-37 of the old bot (the nine snarky lines).
    private static readonly string[] OldBotLines =
    [
        "Check out {displayname}! {Subject} {verb:has|have} some great {game} content. Go give {object} a follow! {Subject} {tense} practically a pro, or at least {subject} {verb:plays|play} one on Twitch.",
        "Yo, peep this! {displayname} {tense} rocking some {game} stuff. Go give {object} a follow! {Subject} {tense} so good, it's almost annoying.",
        "Attention, earthlings! {displayname} has {game} videos you need to see. Go give {object} a follow! {Subject} {tense} probably putting on a masterclass, or a clown show - either way, it's entertaining.",
        "Incoming awesome! {displayname} has some {game} action for you. Go give {object} a follow! {Subject} {tense} crushing it, or at least {subject} {verb:looks|look} like {subject} {presentTense}.",
        "Don't walk, run! {displayname} has more {game} than you can handle. Go give {object} a follow! {Subject} {tense} definitely worth interrupting your snack for.",
        "Our resident legend, {displayname}, has awesome {game}! Go give {object} a follow! {Subject} {tense} probably about to pull off something epic, or face-plant gloriously.",
        "Heads up, buttercups! {displayname} has some {game} for you. Go give {object} a follow! {Subject} {tense} proving once again that {Subject} {tense} awesome (don't tell {object} I said that).",
        "Guess who's got content? {displayname}! {Subject} {tense} rocking {game}. Go give {object} a follow! {Subject} {tense} bringing the vibes, whether {subject} {verb:likes|like} it or not.",
        "Behold! {displayname} has some solid {game} for you. Go give {object} a follow! {Subject} {tense} gracing us with {possessive} presence and questionable decision-making in {game}.",
    ];

    private static readonly string[] Tones =
    [
        PersonalityTone.Informative,
        PersonalityTone.Friendly,
        PersonalityTone.Sassy,
        PersonalityTone.Hype,
        PersonalityTone.Chill,
    ];

    private readonly TemplateResolver _resolver;
    private readonly IChannelRegistry _registry;

    public ShoutoutLineRenderTests()
    {
        PronounGrammarTestDbContext db = PronounGrammarTestDbContext.New();
        Pronoun itIts = Pronoun("it/its", "it", "its", "its", singular: true);
        Pronoun theyThem = Pronoun("they/them", "they", "them", "their", singular: false);
        Pronoun heThey = Pronoun("he/they", "he", "them", "their", singular: false);
        db.Pronouns.AddRange(itIts, theyThem, heThey);
        db.Users.AddRange(
            Viewer("901", "nomz_bot", "Nomz_Bot", itIts),
            Viewer("902", "theyuser", "TheyUser", theyThem),
            Viewer("903", "hetheyuser", "HeTheyUser", heThey)
        );
        db.SaveChanges();

        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        ServiceProvider provider = services.BuildServiceProvider();
        _registry = Substitute.For<IChannelRegistry>();
        _resolver = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            _registry,
            NullLogger<TemplateResolver>.Instance,
            TimeProvider.System
        );
    }

    private static Pronoun Pronoun(
        string name,
        string subject,
        string obj,
        string possessive,
        bool singular
    ) =>
        new()
        {
            Name = name,
            Subject = subject,
            Object = obj,
            Possessive = possessive,
            GenderedTerm = "person",
            Singular = singular,
        };

    private static User Viewer(string id, string login, string display, Pronoun pronoun) =>
        new()
        {
            TwitchUserId = id,
            Username = login,
            UsernameNormalized = login,
            DisplayName = display,
            Pronoun = pronoun,
        };

    private static Dictionary<string, string> Seeds(
        string login,
        string display,
        bool targetLive,
        string game = "Chess"
    ) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["target"] = login,
            ["target.name"] = display,
            ["target.link"] = $"twitch.tv/{login}",
            ["target.game"] = game,
            ["game"] = game,
            ["title"] = "Rated blitz",
            ["status"] = targetLive ? "live" : "offline",
            ["target.isLive"] = targetLive ? "true" : "false",
        };

    private Task<string> RenderAsync(string template, Dictionary<string, string> seeds)
    {
        _registry
            .Get(Channel)
            .Returns(
                new ChannelContext
                {
                    BroadcasterId = Channel,
                    TwitchChannelId = "tw-channel",
                    ChannelName = "stoney",
                    IsLive = true,
                }
            );
        return _resolver.ResolveAsync(template, seeds, Channel);
    }

    [Fact]
    public async Task An_it_target_who_is_live_in_a_game_renders_it_is_and_its()
    {
        string line = await RenderAsync(OwnerTemplate, Seeds("nomz_bot", "Nomz_Bot", true));

        line.Should().Be("Check out @Nomz_Bot! it is streaming Chess. Go give its a follow!");
    }

    [Fact]
    public async Task An_offline_target_renders_was_even_when_the_broadcaster_is_live()
    {
        string line = await RenderAsync(OwnerTemplate, Seeds("nomz_bot", "Nomz_Bot", false));

        line.Should().Be("Check out @Nomz_Bot! it was streaming Chess. Go give its a follow!");
    }

    [Fact]
    public async Task A_they_them_target_renders_they_are_and_they_were()
    {
        string live = await RenderAsync(OwnerTemplate, Seeds("theyuser", "TheyUser", true));
        string offline = await RenderAsync(OwnerTemplate, Seeds("theyuser", "TheyUser", false));

        live.Should().Be("Check out @TheyUser! they are streaming Chess. Go give them a follow!");
        offline
            .Should()
            .Be("Check out @TheyUser! they were streaming Chess. Go give them a follow!");
    }

    [Fact]
    public async Task The_target_forms_and_the_bare_forms_of_game_and_link_render_the_same_and_follow_the_target_state()
    {
        const string Bare = "{game} {link} {tense} {status}";
        const string Targeted = "{target.game} {target.link} {tense} {status}";

        string bareOffline = await RenderAsync(Bare, Seeds("nomz_bot", "Nomz_Bot", false));
        string targetedOffline = await RenderAsync(Targeted, Seeds("nomz_bot", "Nomz_Bot", false));
        string targetedLive = await RenderAsync(Targeted, Seeds("nomz_bot", "Nomz_Bot", true));

        bareOffline.Should().Be("Chess twitch.tv/nomz_bot was offline");
        targetedOffline.Should().Be(bareOffline);
        targetedLive.Should().Be("Chess twitch.tv/nomz_bot is live");
    }

    [Fact]
    public async Task A_he_they_target_keeps_subject_and_verb_in_agreement()
    {
        string line = await RenderAsync(
            "{Subject} {tense} here and {subject} {verb:plays|play}.",
            Seeds("hetheyuser", "HeTheyUser", true)
        );

        line.Should().Be("He is here and he plays.");
    }

    [Fact]
    public async Task The_owner_template_the_tone_catalogue_and_the_old_bot_lines_leave_no_brace_behind()
    {
        // The importer maps the old bot's {displayname} to {name}; the lines render the way they are stored.
        List<string> templates =
        [
            OwnerTemplate,
            .. OldBotLines.Select(l => l.Replace("{displayname}", "{name}")),
        ];
        foreach ((string key, string slot) in ToneTemplateCatalog.AllSlots())
        {
            if (key != BuiltinResponseSlots.Shoutout.Key)
                continue;
            foreach (string tone in Tones)
                templates.AddRange(ToneTemplateCatalog.Get(tone, key, slot));
        }

        List<string> leftovers = [];
        foreach (string template in templates)
        foreach (bool live in new[] { true, false })
        {
            string line = await RenderAsync(template, Seeds("nomz_bot", "Nomz_Bot", live));
            if (line.Contains('{') || line.Contains('}'))
                leftovers.Add(line);
        }

        leftovers.Should().BeEmpty();
    }
}
