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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Discord.EventHandlers;

/// <summary>
/// The shared shape of a Discord trigger handler (discord.md §2), the same flow
/// <see cref="DiscordGoLiveNotificationHandler"/> runs for <c>go_live</c>. A subclass names its trigger, the
/// dedupe key and the placeholder values for one domain event. This base hands them to
/// <see cref="IDiscordNotificationDispatcher"/>, which gates on both-opt-in, dedupes, renders the rule's
/// message and posts through the bot's gateway, so a lost channel access repairs itself in one place.
/// Posting is best-effort: a missing rule (<c>NOT_FOUND</c>) is the expected no-op and any other failure is
/// logged, never thrown, so it can not disturb the event flow.
/// </summary>
public abstract class DiscordTriggerNotificationHandler<TEvent> : IEventHandler<TEvent>
    where TEvent : DomainEventBase
{
    private readonly IDiscordNotificationDispatcher _dispatcher;
    private readonly IOutboundSanctionAccessor _sanctions;
    private readonly ILogger _logger;

    protected DiscordTriggerNotificationHandler(
        IDiscordNotificationDispatcher dispatcher,
        IOutboundSanctionAccessor sanctions,
        ILogger logger
    )
    {
        _dispatcher = dispatcher;
        _sanctions = sanctions;
        _logger = logger;
    }

    /// <summary>The stored trigger name the rule is saved under, for example <c>follow</c>.</summary>
    protected abstract string Trigger { get; }

    /// <summary>
    /// One key per real-world occurrence. The platform's own timestamp is part of it, so a redelivered
    /// event dedupes while a later, separate occurrence still posts.
    /// </summary>
    protected abstract string DedupeKeyOf(TEvent @event);

    /// <summary>The placeholder values for this event. The names must match the trigger's catalog entry.</summary>
    protected abstract Dictionary<string, string> TemplateDataOf(TEvent @event);

    /// <summary>
    /// A new template data bag holding the viewer the event is about: <c>user</c> the shown name,
    /// <c>user.name</c> the login, <c>user.id</c> the platform id. Same rule as every other trigger.
    /// </summary>
    protected static Dictionary<string, string> ViewerData(
        string? id,
        string? login,
        string? displayName
    )
    {
        Dictionary<string, string> data = [];
        TemplateSubject.SeedUser(data, id, login, displayName);
        return data;
    }

    /// <summary>True when the event must not post, for example a gifted sub that the gift trigger announces.</summary>
    protected virtual bool IsSkipped(TEvent @event) => false;

    public async Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event.BroadcasterId == Guid.Empty || IsSkipped(@event))
            return;

        Dictionary<string, string> templateData = TemplateDataOf(@event);

        // Posting to the broadcaster's Discord server is allowed here because the broadcaster configured a
        // rule for this trigger; the sanction names that setting so the post is traceable to it.
        using IDisposable sanction = _sanctions.Begin(
            OutboundSanction.ChannelConfiguration($"discord_{Trigger}_notification")
        );

        Result<DiscordDispatchOutcomeDto> result = await _dispatcher.DispatchAsync(
            new(
                @event.BroadcasterId,
                Trigger,
                DedupeKeyOf(@event),
                StreamId: null,
                new Dictionary<string, string>(templateData, StringComparer.OrdinalIgnoreCase)
            ),
            cancellationToken
        );

        if (result.IsFailure && result.ErrorCode != "NOT_FOUND")
            _logger.LogWarning(
                "Discord {Trigger} dispatch failed for {BroadcasterId}: {Error}",
                Trigger,
                @event.BroadcasterId,
                result.ErrorMessage
            );
    }
}
