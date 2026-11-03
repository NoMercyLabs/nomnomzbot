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

using NomNomzBot.Application.Chat.Services;

namespace NomNomzBot.Infrastructure.Commands.TestFire;

/// <summary>
/// Hands every reply to the real dispatcher and remembers the lines that were delivered, so a test fire can say
/// which line viewers saw.
/// </summary>
internal sealed class RecordingReplyDispatcher(IReplyDispatcher inner) : IReplyDispatcher
{
    private readonly List<string> _lines = [];

    /// <summary>The lines the dispatcher delivered to chat or to the chat overlay, in order.</summary>
    public IReadOnlyList<string> DeliveredLines => _lines;

    public async Task<ReplyOutcome> DispatchAsync(
        ReplyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ReplyOutcome outcome = await inner.DispatchAsync(request, cancellationToken);
        if (outcome.SentToChat || outcome.SentToOverlay)
            _lines.Add(request.Text);
        return outcome;
    }
}
