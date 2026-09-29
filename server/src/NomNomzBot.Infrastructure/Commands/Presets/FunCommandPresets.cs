// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Dtos;

namespace NomNomzBot.Infrastructure.Commands.Presets;

/// <summary>
/// The fun-command preset pack (S068f — the old bot's seeded fun commands): harmless template commands a
/// fresh channel gets so it is not a blank slate. The one definition both the onboarding seed and the
/// dashboard's "Reset to preset" read, so the two can never disagree. A preset's key is its seeded name.
/// </summary>
internal static class FunCommandPresets
{
    public static readonly IReadOnlyList<CreateCommandDto> All =
    [
        new()
        {
            Name = "8ball",
            Description = "Ask the magic 8-ball a yes/no question.",
            TemplateResponses =
            [
                "🎱 It is certain.",
                "🎱 Without a doubt.",
                "🎱 You may rely on it.",
                "🎱 Ask again later.",
                "🎱 Cannot predict now.",
                "🎱 Don't count on it.",
                "🎱 My reply is no.",
                "🎱 Outlook not so good.",
            ],
        },
        new()
        {
            Name = "hug",
            Description = "Give someone a hug.",
            TemplateResponse = "{{user.name}} gives {{args.1}} a big warm hug! 🤗",
        },
        new()
        {
            Name = "slap",
            Description = "Slap someone with a trout.",
            TemplateResponse = "{{user.name}} slaps {{args.1}} around a bit with a large trout! 🐟",
        },
        new()
        {
            Name = "ping",
            Description = "Check that the bot is alive.",
            TemplateResponse = "🏓 Pong!",
        },
        new()
        {
            Name = "rps",
            Description = "Play rock, paper, scissors against the bot.",
            TemplateResponses =
            [
                "🪨 The bot throws Rock!",
                "📄 The bot throws Paper!",
                "✂️ The bot throws Scissors!",
            ],
        },
        new()
        {
            Name = "compliment",
            Description = "Give someone a compliment.",
            TemplateResponses =
            [
                "{{args.1}}, you're doing amazing today! ✨",
                "{{args.1}} lights up the room! 🌟",
                "{{args.1}} has impeccable taste in streams. 😎",
                "{{args.1}} deserves a round of applause! 👏",
            ],
        },
    ];

    public static CreateCommandDto? Find(string key) =>
        All.FirstOrDefault(p => p.Name == key.ToLowerInvariant());
}
