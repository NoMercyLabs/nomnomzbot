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

using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Domain.Chat.Events;

namespace NomNomzBot.Infrastructure.Chat.EventHandlers;

/// <summary>
/// Interprets the text a viewer typed after a command once, at input. The whole <c>{args}</c> text becomes the
/// resolved string that every later action reads; the single words <c>{args.N}</c> stay as typed. The service
/// decides whether the channel and the viewer allow helpers, so a viewer who may not gets the text unchanged.
/// </summary>
internal static class ViewerArgsInterpreter
{
    public static async Task InterpretAsync(
        IViewerInputTemplateService templates,
        ChatMessageReceivedEvent @event,
        IDictionary<string, string> variables,
        CancellationToken cancellationToken
    )
    {
        if (!variables.TryGetValue("args", out string? typed) || typed.Length == 0)
            return;

        variables["args"] = await templates.ResolveRewardInputAsync(
            @event.BroadcasterId,
            new ViewerInputIdentity(@event.UserId, @event.UserLogin, @event.UserDisplayName),
            typed,
            isTts: false,
            audit: true,
            cancellationToken
        );
    }
}
