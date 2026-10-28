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

using NomNomzBot.Domain.Commands.ValueObjects;

namespace NomNomzBot.Application.Chat.Services;

/// <summary>
/// The one place an authored reply is delivered. It sends to the platform chat, the chat overlay, both or
/// neither, as the reply's <see cref="ReplyDelivery"/> says, so no surface needs its own delivery logic.
/// </summary>
public interface IReplyDispatcher
{
    Task<ReplyOutcome> DispatchAsync(
        ReplyRequest request,
        CancellationToken cancellationToken = default
    );
}

/// <summary>One reply to deliver.</summary>
public sealed record ReplyRequest
{
    public required Guid BroadcasterId { get; init; }

    /// <summary>The reply as plain text for the platform chat.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// The reply for the chat overlay, resolved on its own when the overlay renders HTML (so its template
    /// variables could be HTML-encoded first). Null = the overlay shows <see cref="Text"/>.
    /// </summary>
    public string? OverlayText { get; init; }

    public required ReplyDelivery Delivery { get; init; }

    /// <summary>
    /// The display name of the viewer whose command asked for this reply. The chat overlay line is attributed to
    /// them ("requested by ..."); the platform chat reply is not, because it already answers that viewer's message.
    /// Null = no attribution.
    /// </summary>
    public string? RequestedBy { get; init; }

    /// <summary>
    /// The inbound chat message this reply answers. Set: the chat leg answers on that platform as a native
    /// reply. Null: the chat leg sends a plain message on the channel's own platform.
    /// </summary>
    public ReplyOrigin? Origin { get; init; }
}

/// <summary>The inbound message a reply answers: the platform it arrived on and who wrote it.</summary>
/// <param name="Provider">The platform the message arrived on (twitch | kick | youtube).</param>
/// <param name="MessageId">The platform message id to thread the reply under; empty = send a plain message.</param>
/// <param name="UserDisplayName">The sender's display name, for the mention when the reply form is rejected.</param>
/// <param name="UserLogin">The sender's login, for the same mention.</param>
public sealed record ReplyOrigin(
    string Provider,
    string MessageId,
    string UserDisplayName,
    string UserLogin
);

/// <summary>What a dispatch did.</summary>
/// <param name="SentToChat">The platform chat accepted the reply.</param>
/// <param name="SentToOverlay">At least one chat widget got the overlay line.</param>
/// <param name="OverlayMissing">The reply wanted the overlay but no chat widget was open, so nothing went there.</param>
public sealed record ReplyOutcome(bool SentToChat, bool SentToOverlay, bool OverlayMissing)
{
    /// <summary>
    /// Whether the reply counts as delivered for <paramref name="delivery"/>: chat needs the chat send, overlay
    /// needs an open overlay, both needs either, none has nothing to deliver.
    /// </summary>
    public bool Succeeded(ReplyDelivery delivery) =>
        delivery.Target switch
        {
            ReplyTarget.Chat => SentToChat,
            ReplyTarget.Overlay => SentToOverlay,
            ReplyTarget.Both => SentToChat || SentToOverlay,
            _ => true,
        };
}
