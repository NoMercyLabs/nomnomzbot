// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
        string slot = BuiltinResponseSlots.PlaybackControl.FailureSlotFor(failure.ErrorCode);
        string reply = ToneTemplateCatalog.Get(PersonalityTone.Informative, builtinKey, slot)[0];
        ctx.RepliedToChat |= await chat.SendMessageAsync(
            ctx.BroadcasterId,
            reply,
            ctx.CancellationToken
        );
    }
}
