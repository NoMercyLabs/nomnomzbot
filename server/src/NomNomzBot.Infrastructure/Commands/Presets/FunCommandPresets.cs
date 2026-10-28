// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
            TemplateResponse = "{user.name} gives {args} a big warm hug! 🤗",
        },
        new()
        {
            Name = "slap",
            Description = "Slap someone with a trout.",
            TemplateResponse = "{user.name} slaps {args} around a bit with a large trout! 🐟",
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
                "{args}, you're doing amazing today! ✨",
                "{args} lights up the room! 🌟",
                "{args} has impeccable taste in streams. 😎",
                "{args} deserves a round of applause! 👏",
            ],
        },
    ];

    public static CreateCommandDto? Find(string key) =>
        All.FirstOrDefault(p => p.Name == key.ToLowerInvariant());
}
