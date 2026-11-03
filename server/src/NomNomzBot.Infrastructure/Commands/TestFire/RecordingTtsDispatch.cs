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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Infrastructure.Platform;

namespace NomNomzBot.Infrastructure.Commands.TestFire;

/// <summary>
/// Hands every speech request to the real dispatcher and remembers each one with its refusal reason, so a test
/// fire can say what was read aloud and why a line was not. The executor only logs a refusal; this keeps it.
/// </summary>
[RegisteredByHand]
internal sealed class RecordingTtsDispatch(ITtsDispatchService inner) : ITtsDispatchService
{
    private readonly List<EventResponseTestSpeechLine> _lines = [];

    /// <summary>Every request this dispatcher saw, in order, with the refusal message when it was refused.</summary>
    public IReadOnlyList<EventResponseTestSpeechLine> Lines => _lines;

    public async Task<Result<TtsDispatchOutcome>> RequestSpeakAsync(
        TtsSpeakRequest request,
        CancellationToken ct = default
    )
    {
        Result<TtsDispatchOutcome> result = await inner.RequestSpeakAsync(request, ct);
        _lines.Add(new(request.Text, result.IsFailure ? result.ErrorMessage : null));
        return result;
    }

    public Task<string?> ResolveVoiceAsync(
        Guid broadcasterId,
        string requestedByTwitchUserId,
        string? voiceIdOverride,
        TtsSpeaker speaker = TtsSpeaker.Bot,
        CancellationToken ct = default
    ) =>
        inner.ResolveVoiceAsync(
            broadcasterId,
            requestedByTwitchUserId,
            voiceIdOverride,
            speaker,
            ct
        );

    public Task<Result> ApproveAsync(
        Guid broadcasterId,
        Guid queueEntryId,
        Guid reviewedByUserId,
        CancellationToken ct = default
    ) => inner.ApproveAsync(broadcasterId, queueEntryId, reviewedByUserId, ct);

    public Task<Result> RejectAsync(
        Guid broadcasterId,
        Guid queueEntryId,
        Guid reviewedByUserId,
        CancellationToken ct = default
    ) => inner.RejectAsync(broadcasterId, queueEntryId, reviewedByUserId, ct);

    public Task<Result<PagedList<TtsQueueEntryDto>>> GetPendingQueueAsync(
        Guid broadcasterId,
        int page,
        int pageSize,
        CancellationToken ct = default
    ) => inner.GetPendingQueueAsync(broadcasterId, page, pageSize, ct);
}
