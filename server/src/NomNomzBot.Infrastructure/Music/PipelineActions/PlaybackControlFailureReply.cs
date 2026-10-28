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

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Music.PipelineActions;

/// <summary>
/// Tells chat why a <c>song_pause</c>/<c>song_resume</c> step failed — the same reply the <c>!pause</c>/
/// <c>!resume</c> commands give for that failure code, so "the Audio widget isn't open" is never silent.
/// </summary>
internal static class PlaybackControlFailureReply
{
    public static async Task SendAsync(
        IChatProvider chat,
        PipelineExecutionContext ctx,
        string builtinKey,
        Result failure
    )
    {
        string slot = BuiltinResponseSlots.PlaybackControl.FailureSlotFor(
            failure.ErrorCode,
            failure.ErrorData
        );
        string reply = ToneTemplateCatalog.Get(PersonalityTone.Informative, builtinKey, slot)[0];
        ctx.RepliedToChat |= await chat.SendMessageAsync(
            ctx.BroadcasterId,
            reply,
            ctx.CancellationToken
        );
    }
}
