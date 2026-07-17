// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Federation;
using NomNomzBot.Domain.Federation.Enums;

namespace NomNomzBot.Infrastructure.Federation;

/// <summary>
/// Envelope → typed dispatch (federation-oidc.md §3.6/§3.7). Selects the one handler whose <c>Type</c> matches the
/// envelope (unknown ⇒ <c>schema_invalid</c>), resolves the local target channel(s) through the opt-in service
/// (directed target, or a fan-out over every accepting channel), and invokes the handler once per resolved target.
/// It routes; the owning subsystem's handler applies. Zero targets is an accepted noop, not an error.
/// </summary>
public sealed class FederationInboundTranslator(
    IEnumerable<IFederationInboundHandler> handlers,
    IFederationOptInService optIns
) : IFederationInboundTranslator
{
    public async Task<Result<int>> TranslateAndApplyAsync(
        Guid peerId,
        FederationEventEnvelope envelope,
        CancellationToken cancellationToken = default
    )
    {
        IFederationInboundHandler? handler = handlers.FirstOrDefault(h =>
            h.Type == envelope.FederatedEventType
        );
        if (handler is null)
            return Result.Failure<int>(
                $"No inbound handler is registered for '{envelope.FederatedEventType}'.",
                "schema_invalid"
            );

        IReadOnlyList<Guid> targets = await ResolveTargetsAsync(
            peerId,
            envelope,
            handler.GatingOptInType,
            cancellationToken
        );

        int applied = 0;
        foreach (Guid target in targets)
        {
            Result result = await handler.ApplyAsync(peerId, target, envelope, cancellationToken);
            if (result.IsFailure)
                return Result.Failure<int>(result.ErrorMessage, result.ErrorCode); // schema_invalid propagates
            applied++;
        }
        return Result.Success(applied);
    }

    /// <summary>
    /// Directed envelope → the single named channel iff it holds the accept opt-in; broadcast envelope → every
    /// channel that accepts this type from the peer. The opt-in service owns the accept predicate (peer-trust +
    /// null-peer wildcard + direction), so the translator never re-derives it.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> ResolveTargetsAsync(
        Guid peerId,
        FederationEventEnvelope envelope,
        string gatingOptInType,
        CancellationToken cancellationToken
    )
    {
        if (envelope.TargetBroadcasterId is Guid directed)
        {
            bool permitted = (
                await optIns.IsActionPermittedAsync(
                    directed,
                    peerId,
                    gatingOptInType,
                    FederationDirection.Accept,
                    cancellationToken
                )
            ).Value;
            return permitted ? [directed] : [];
        }

        return (
            await optIns.ListAcceptingBroadcasterIdsAsync(
                peerId,
                gatingOptInType,
                cancellationToken
            )
        ).Value;
    }
}
