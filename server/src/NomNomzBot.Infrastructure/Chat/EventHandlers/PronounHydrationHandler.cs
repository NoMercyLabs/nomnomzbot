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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Chat.EventHandlers;

/// <summary>
/// Lazily hydrates a viewer's pronouns the first time they appear in chat (spec D3) — the wiring
/// <see cref="IPronounResolutionService"/> was missing until now (it had zero callers). Resolves/creates the
/// viewer's internal <c>User</c> row (mirrors <c>ChatEarningHandler</c>'s own get-or-create), then delegates to
/// <see cref="IPronounResolutionService.ResolveAndApplyAsync"/>, which itself cache-gates the alejo.io lookup to
/// once per 24h per viewer — so this handler is cheap to run on every message even though it fires unconditionally.
/// A provider outage, timeout, or DB error here must never break chat ingest: every failure is caught and logged
/// at Debug so command execution and persistence (the other <see cref="ChatMessageReceivedEvent"/> handlers) are
/// unaffected. This is defense in depth on top of the EventBus's own per-handler isolation (it already isolates
/// one handler's exception from the others), so a throwing provider can never surface here either.
/// </summary>
public sealed class PronounHydrationHandler(
    IUserService userService,
    IPronounResolutionService pronouns,
    ILogger<PronounHydrationHandler> logger
) : IEventHandler<ChatMessageReceivedEvent>
{
    public async Task HandleAsync(
        ChatMessageReceivedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.BroadcasterId == Guid.Empty || string.IsNullOrEmpty(@event.UserId))
            return;

        try
        {
            Result<UserDto> userResult = await userService.GetOrCreateAsync(
                @event.UserId,
                @event.UserLogin,
                @event.UserDisplayName,
                cancellationToken
            );
            if (userResult.IsFailure || !Guid.TryParse(userResult.Value.Id, out Guid viewerUserId))
                return;

            await pronouns.ResolveAndApplyAsync(viewerUserId, @event.UserLogin, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Chat ingest is shutting down / request aborted — nothing to log.
        }
        catch (Exception ex)
        {
            logger.LogDebug(
                ex,
                "Pronoun hydration failed for {UserId} ({Login}) — chat ingest continues unaffected",
                @event.UserId,
                @event.UserLogin
            );
        }
    }
}
