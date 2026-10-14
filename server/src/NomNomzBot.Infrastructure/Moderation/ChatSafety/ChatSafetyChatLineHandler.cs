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
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Moderation.ChatSafety;

/// <summary>
/// Counts each chat line on the channel's safety meter. The first line of the stream from an account
/// younger than 7 days also counts as a new account; the age is read only for that first line.
/// </summary>
public sealed class ChatSafetyChatLineHandler : IEventHandler<ChatMessageReceivedEvent>
{
    private readonly ChatSafetyMeter _meter;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _time;

    public ChatSafetyChatLineHandler(
        ChatSafetyMeter meter,
        IServiceScopeFactory scopeFactory,
        TimeProvider time
    )
    {
        _meter = meter;
        _scopeFactory = scopeFactory;
        _time = time;
    }

    public async Task HandleAsync(ChatMessageReceivedEvent @event, CancellationToken ct)
    {
        if (@event.BroadcasterId == Guid.Empty)
            return;

        _meter.RecordChatLine(@event.BroadcasterId);
        if (!_meter.MarkFirstLine(@event.BroadcasterId, @event.UserId))
            return;

        using IServiceScope scope = _scopeFactory.CreateScope();
        DateTime? createdAt = await scope
            .ServiceProvider.GetRequiredService<IApplicationDbContext>()
            .Users.AsNoTracking()
            .Where(u => u.TwitchUserId == @event.UserId)
            .Select(u => u.AccountCreatedAt)
            .FirstOrDefaultAsync(ct);

        if (ChatSafetyMeter.IsNewAccount(createdAt, _time.GetUtcNow()))
            _meter.RecordNewAccount(@event.BroadcasterId);
    }
}
