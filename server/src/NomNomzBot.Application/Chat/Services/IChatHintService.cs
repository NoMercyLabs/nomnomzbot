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

namespace NomNomzBot.Application.Chat.Services;

/// <summary>
/// One chat message to resolve hints for: who wrote it, on which platform, when, and when the stream it was sent
/// in started (null when the channel was offline). Stored chat rows carry no stream id, so the stream is known by
/// its start time.
/// </summary>
public sealed record ChatHintRequest(
    string MessageId,
    string Provider,
    string UserId,
    DateTime SentAtUtc,
    DateTime? StreamStartUtc
);

/// <summary>
/// The facts a chat client turns into hints on one line. <see cref="FirstEver"/> is the chatter's first stored message
/// in this channel, <see cref="FirstOfDay"/> the first one of that calendar day in the channel's timezone,
/// <see cref="FirstOfStream"/> the first one of that stream. <see cref="AccountCreatedAtUtc"/> is when the platform
/// account was made (null when unknown). The client decides what each fact looks like and when it shows.
/// </summary>
public sealed record ChatHints(
    bool FirstEver,
    bool FirstOfDay,
    bool FirstOfStream,
    DateTime? AccountCreatedAtUtc
);

/// <summary>
/// Works out <see cref="ChatHints"/> for chat messages from the stored chat history and the user records, in a
/// few queries for the whole batch. The live feed asks for one message, a history page asks for the whole page,
/// so both show the same hints for the same message.
/// </summary>
public interface IChatHintService
{
    /// <summary>
    /// The hints of each requested message, by message id. <paramref name="timezone"/> is the channel's IANA zone
    /// (null or unknown means UTC) and decides where a calendar day starts.
    /// </summary>
    Task<IReadOnlyDictionary<string, ChatHints>> ResolveAsync(
        Guid broadcasterId,
        string? timezone,
        IReadOnlyList<ChatHintRequest> messages,
        CancellationToken ct = default
    );
}
