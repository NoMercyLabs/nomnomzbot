// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Commands.EventHandlers;

/// <summary>
/// Onboarding seed job (Commands domain, S068f — legacy builtins audit): seeds a small, fixed pack of
/// harmless fun custom commands (<c>!8ball</c>, <c>!hug</c>, <c>!slap</c>, <c>!ping</c>, <c>!rps</c>,
/// <c>!compliment</c>) for a newly-onboarded channel, so a fresh channel isn't a blank slate on first
/// setup — mirroring the old bot's seeded fun-command preset pack. These are real <c>Command</c> rows
/// created through <see cref="ICommandService.CreateAsync"/> — the same path the dashboard's "new
/// command" form uses — never a hand-written EF/SQL insert. Idempotent: <see cref="ICommandService.CreateAsync"/>
/// already rejects a duplicate trigger name with <c>ALREADY_EXISTS</c>, so re-firing onboarding (or the
/// startup backfill) for an already-seeded channel, or a channel where the streamer renamed/kept one of
/// these names for their own command, never duplicates or clobbers anything. Independently resilient —
/// one preset's failure is caught and logged, never propagated, so it cannot block the rest of the pack
/// or affect the other onboarding seed jobs.
/// </summary>
public sealed class FunCommandPresetPackSeedOnOnboardingHandler(
    ICommandService commands,
    ILogger<FunCommandPresetPackSeedOnOnboardingHandler> logger
) : IEventHandler<ChannelOnboardedEvent>
{
    private static readonly IReadOnlyList<CreateCommandDto> Presets =
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

    public async Task HandleAsync(ChannelOnboardedEvent @event, CancellationToken ct = default)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        string broadcasterId = @event.BroadcasterId.ToString();

        logger.LogInformation(
            "Onboarding seed (fun command preset pack): seeding {Count} preset command(s) for {BroadcasterId} ({Name})",
            Presets.Count,
            @event.BroadcasterId,
            @event.Name
        );

        int seeded = 0;
        int skipped = 0;
        foreach (CreateCommandDto preset in Presets)
        {
            try
            {
                Result<CommandDto> result = await commands.CreateAsync(broadcasterId, preset, ct);

                if (result.IsSuccess)
                {
                    seeded++;
                }
                else if (result.ErrorCode == "ALREADY_EXISTS")
                {
                    skipped++;
                }
                else
                {
                    logger.LogWarning(
                        "Onboarding seed (fun command preset pack): !{Name} creation returned a failure for {BroadcasterId}: {Error} ({Code})",
                        preset.Name,
                        @event.BroadcasterId,
                        result.ErrorMessage,
                        result.ErrorCode
                    );
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(
                    ex,
                    "Onboarding seed (fun command preset pack): !{Name} failed for {BroadcasterId}",
                    preset.Name,
                    @event.BroadcasterId
                );
            }
        }

        logger.LogInformation(
            "Onboarding seed (fun command preset pack): completed for {BroadcasterId} — {Seeded} seeded, {Skipped} already present",
            @event.BroadcasterId,
            seeded,
            skipped
        );
    }
}
