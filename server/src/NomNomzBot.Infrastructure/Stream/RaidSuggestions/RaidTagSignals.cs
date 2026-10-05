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
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Stream.RaidSuggestions;

/// <summary>
/// Tag and title-token patterns that tell real software work from game development. A tag or any
/// title token that matches counts. Copied from the legacy bot's RaidSuggestionService.
/// </summary>
internal static class RaidTagSignals
{
    /// <summary>
    /// Software-dev signals. Languages common in game dev (csharp, cpp, java, lua, javascript, rust)
    /// are left out on purpose because they are ambiguous.
    /// </summary>
    private static readonly Regex SoftwarePattern = new(
        @"^(programming|softwaredev(elopment)?|softwareengineer(ing)?|webdev(elopment)?|"
            + @"coding|code|developer|backend|frontend|fullstack(dev)?|devops|sre|"
            + @"systemsprogramming|datascience|machinelearning|"
            // Languages with low game-dev overlap
            + @"python|typescript|ruby|php|golang|elixir|erlang|haskell|scala|clojure|"
            + @"kotlin|swift|dart|flutter|elm|fsharp|ocaml|julia|perl|crystal|nim|zig|"
            + @"r(language)?|"
            // Web frameworks
            + @"react(js)?|vue(js)?|angular|svelte(kit)?|next(js)?|nuxt(js)?|astro|remix|"
            + @"django|flask|fastapi|rails|laravel|symfony|spring(boot)?|aspnetcore|"
            + @"nodejs|node|deno|bun|express(js)?|"
            // Markup, styling, frontend tooling
            + @"html5?|css3?|scss|sass|less|tailwind(css)?|bootstrap|jquery|webpack|vite|"
            // Databases
            + @"postgres(ql)?|mysql|mariadb|mongodb|redis|elasticsearch|sqlite|cassandra|dynamodb|"
            // Infra / cloud
            + @"kubernetes|k8s|docker|terraform|ansible|helm|nginx|apache|"
            + @"aws|azure|gcp|cloudflare|digitalocean|heroku|vercel|netlify|"
            + @"prometheus|grafana|kafka|rabbitmq|"
            // Shells / editors / tools
            + @"bash|zsh|fish|powershell|emacs|neovim|vim|git|"
            // Architecture / API
            + @"microservices|graphql|restapi|api|grpc|websockets)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    /// <summary>Game-dev signals: explicit tags, game engines and genre tags.</summary>
    private static readonly Regex GameDevPattern = new(
        @"^(gamedev(elopment)?|indie(game)?dev|gamejam|"
            // Engines / game-specific tooling
            + @"godot|unity|unrealengine|unreal|gamemaker(studio)?|construct(2|3)?|defold|lovd2|pygame|phaser|raylib|monogame|pixijs|playcanvas|"
            // Genres
            + @"roguelike|roguelite|deckbuilder|cardgame|metroidvania|platformer|shmup|bullethell|fightinggame|"
            + @"jrpg|arpg|crpg|wrpg|rpgmaker|"
            + @"survivalgame|horrorgame|puzzlegame|racinggame|simulationgame|tycoon|sandboxgame|"
            + @"fps|rts|tps|moba|mmo(rpg)?|battleroyale)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex TitleTokenSplit = new(@"[^a-zA-Z0-9+#]+", RegexOptions.Compiled);

    public static bool IsGameDev(TwitchStream stream) => HasMatch(stream, GameDevPattern);

    public static bool IsSoftware(TwitchStream stream) => HasMatch(stream, SoftwarePattern);

    private static bool HasMatch(TwitchStream stream, Regex pattern)
    {
        foreach (string tag in stream.Tags)
        {
            if (pattern.IsMatch(tag))
                return true;
        }

        if (string.IsNullOrWhiteSpace(stream.Title))
            return false;

        foreach (string token in TitleTokenSplit.Split(stream.Title))
        {
            if (token.Length > 0 && pattern.IsMatch(token))
                return true;
        }

        return false;
    }
}
