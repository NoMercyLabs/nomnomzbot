// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Contracts.Federation;
using NomNomzBot.Domain.Federation.Entities;
using NomNomzBot.Domain.Federation.Enums;
using NomNomzBot.Domain.Federation.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Federation;

/// <summary>
/// The inbound leg of the remote event bus (federation-oidc.md §3.5). Fail-closed at every gate: a signed peer
/// envelope is verified, bound to a trusted peer, replay-guarded, opt-in-gated (directed), journaled with
/// <c>Source=federation</c>, then translated + applied. Every accept emits <see cref="FederatedEventReceivedEvent"/>;
/// every rejection emits <see cref="FederatedEventRejectedEvent"/> carrying the same reason code returned to the
/// caller. The journal is the end-to-end dedupe source of truth — a re-delivered <c>EventId</c> is a <c>replay</c>.
/// </summary>
public sealed class FederationInboundGateway(
    IApplicationDbContext db,
    IFederationEventSigner signer,
    IFederationOptInService optIns,
    IFederationInboundTranslator translator,
    IEventJournal journal,
    IEventBus eventBus,
    IEnumerable<IFederationInboundHandler> handlers
) : IFederationInboundGateway
{
    private const string Source = "federation";

    public async Task<Result<FederationInboundOutcome>> ReceiveInboundAsync(
        Guid peerId,
        FederationEventEnvelope envelope,
        FederationSignature signature,
        CancellationToken cancellationToken = default
    )
    {
        // Gate 1 — per-message signature (rsa-sha256, fails closed on algorithm/key/signature).
        Result verified = await signer.VerifyAsync(peerId, envelope, signature, cancellationToken);
        if (verified.IsFailure)
            return await RejectAsync(
                peerId,
                envelope,
                verified.ErrorCode ?? "signature_invalid",
                null,
                cancellationToken
            );

        // Gate 2 — the peer must exist, be trusted, and be the instance it claims to be (blocks a trusted
        // peer relaying another instance's envelope).
        FederationPeer? peer = await db.FederationPeers.FirstOrDefaultAsync(
            p => p.Id == peerId && p.DeletedAt == null,
            cancellationToken
        );
        if (
            peer is null
            || peer.TrustState != FederationTrustState.Trusted
            || peer.InstanceId != envelope.OriginInstanceId
        )
            return await RejectAsync(
                peerId,
                envelope,
                "peer_untrusted",
                peer?.InstanceId,
                cancellationToken
            );

        // Gate 3 — the accept-set is exactly the registered handlers' Types; anything else fails closed.
        IFederationInboundHandler? handler = handlers.FirstOrDefault(h =>
            h.Type == envelope.FederatedEventType
        );
        if (handler is null)
            return await RejectAsync(
                peerId,
                envelope,
                "schema_invalid",
                peer.InstanceId,
                cancellationToken
            );

        // Gate 4 — replay guard: the journal is idempotent on EventId, so an already-journaled id is a replay.
        Result<EventRecord> existing = await journal.GetByEventIdAsync(
            envelope.EventId,
            cancellationToken
        );
        if (existing.IsSuccess)
            return await RejectAsync(
                peerId,
                envelope,
                "replay",
                peer.InstanceId,
                cancellationToken
            );

        // Gate 5 — a DIRECTED envelope must land on a channel that explicitly opted in; otherwise no_opt_in
        // (before journaling). A broadcast envelope (null target) fans out in the translator and a zero-match
        // is an accepted noop, never a rejection.
        if (envelope.TargetBroadcasterId is Guid directed)
        {
            bool permitted = (
                await optIns.IsActionPermittedAsync(
                    directed,
                    peerId,
                    handler.GatingOptInType,
                    FederationDirection.Accept,
                    cancellationToken
                )
            ).Value;
            if (!permitted)
                return await RejectAsync(
                    peerId,
                    envelope,
                    "no_opt_in",
                    peer.InstanceId,
                    cancellationToken
                );
        }

        // Append the claim to the immutable journal (Source=federation) — the audit + dedupe record.
        Result<EventRecord> appended = await journal.AppendAsync(
            new AppendEventRequest(
                envelope.EventId,
                envelope.TargetBroadcasterId,
                envelope.FederatedEventType,
                envelope.SchemaVersion,
                Source,
                envelope.PayloadJson,
                JsonConvert.SerializeObject(
                    new
                    {
                        peerId,
                        originInstanceId = envelope.OriginInstanceId,
                        originBroadcasterId = envelope.OriginBroadcasterId,
                    }
                ),
                envelope.OccurredAt.UtcDateTime
            ),
            cancellationToken
        );
        if (appended.IsFailure)
            return Result.Failure<FederationInboundOutcome>(
                appended.ErrorMessage,
                appended.ErrorCode
            );

        // Translate + apply. A handler that cannot deserialize its payload fails closed as schema_invalid.
        Result<int> applied = await translator.TranslateAndApplyAsync(
            peerId,
            envelope,
            cancellationToken
        );
        if (applied.IsFailure)
            return await RejectAsync(
                peerId,
                envelope,
                applied.ErrorCode ?? "schema_invalid",
                peer.InstanceId,
                cancellationToken
            );

        await eventBus.PublishAsync(
            new FederatedEventReceivedEvent
            {
                BroadcasterId = envelope.TargetBroadcasterId ?? Guid.Empty,
                PeerId = peerId,
                JournalEventId = appended.Value.EventId,
                FederatedEventType = envelope.FederatedEventType,
                TargetBroadcasterId = envelope.TargetBroadcasterId,
                StreamPosition = appended.Value.StreamPosition,
            },
            cancellationToken
        );

        return Result.Success(
            new FederationInboundOutcome(
                appended.Value.EventId,
                appended.Value.StreamPosition,
                applied.Value > 0
            )
        );
    }

    private async Task<Result<FederationInboundOutcome>> RejectAsync(
        Guid peerId,
        FederationEventEnvelope envelope,
        string reason,
        string? peerInstanceId,
        CancellationToken cancellationToken
    )
    {
        await eventBus.PublishAsync(
            new FederatedEventRejectedEvent
            {
                BroadcasterId = envelope.TargetBroadcasterId ?? Guid.Empty,
                PeerId = peerId,
                Reason = reason,
                FederatedEventType = envelope.FederatedEventType,
                PeerInstanceId = peerInstanceId ?? envelope.OriginInstanceId,
            },
            cancellationToken
        );
        return Result.Failure<FederationInboundOutcome>(
            $"Inbound federated event rejected: {reason}.",
            reason
        );
    }
}
