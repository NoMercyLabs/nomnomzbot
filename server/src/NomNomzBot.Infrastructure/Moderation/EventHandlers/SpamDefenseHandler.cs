// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Moderation.EventHandlers;

/// <summary>
/// Puts the spam-defence stack on the live chat path.
///
/// <para>Without this the whole engine is unreached code — the exact shape of a defect found earlier in
/// this campaign, where a dashboard control set a heat threshold that nothing read. A layer nobody
/// calls protects nobody.</para>
///
/// <para>The handler stays thin on purpose: it translates the wire event and hands off. Every decision
/// lives in <see cref="ISpamDefenseService"/>, so there is one place to look when asking why somebody
/// was actioned.</para>
/// </summary>
public sealed class SpamDefenseHandler : IEventHandler<ChatMessageReceivedEvent>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SpamDefenseHandler> _logger;

    public SpamDefenseHandler(IServiceScopeFactory scopeFactory, ILogger<SpamDefenseHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task HandleAsync(ChatMessageReceivedEvent @event, CancellationToken ct)
    {
        if (@event.BroadcasterId == Guid.Empty || string.IsNullOrWhiteSpace(@event.Message))
            return;

        // Deliberately NOT gated on provider. Unlike the older auto-mod handler, evaluation is
        // platform-agnostic — the layers read text and history, not Helix — and the record is worth
        // having everywhere even where enforcement cannot reach yet. Whether a verdict can be ACTED on
        // is the enforcement path's problem, and it is honest about which platforms it covers.
        try
        {
            using IServiceScope scope = _scopeFactory.CreateScope();
            ISpamDefenseService spamDefense =
                scope.ServiceProvider.GetRequiredService<ISpamDefenseService>();

            SpamEvaluationResult? result = await spamDefense.EvaluateAsync(
                new SpamEvaluationRequest(
                    @event.BroadcasterId,
                    @event.Provider,
                    @event.MessageId,
                    @event.UserId,
                    @event.UserDisplayName,
                    @event.Message,
                    @event.IsBroadcaster,
                    @event.IsModerator,
                    @event.IsVip,
                    @event.IsSubscriber
                ),
                ct
            );

            if (result?.DetectionId is not null)
                _logger.LogInformation(
                    "Spam defence: {Outcome} (would have been {WouldHaveBeen}) for {User} in {Channel} — {Reason}",
                    result.Decision.Outcome,
                    result.Decision.WouldHaveBeen,
                    @event.UserLogin,
                    @event.BroadcasterId,
                    result.Decision.Reason
                );
        }
        catch (Exception ex)
        {
            // A failure here must never take down chat ingestion: this handler shares the hot path with
            // command replies and the chat feed.
            _logger.LogError(
                ex,
                "Spam defence evaluation failed for {Channel}",
                @event.BroadcasterId
            );
        }
    }
}
