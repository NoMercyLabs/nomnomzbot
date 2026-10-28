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
