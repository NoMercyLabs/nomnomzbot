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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Chat.EventHandlers;

/// <summary>
/// Stores YouTube's verified flag (<c>authorDetails.isVerified</c>) on the chatter's user row from every
/// YouTube chat line, so the trust score can give a verified channel its boost. Each line refreshes the
/// value, so a channel that loses verification is cleared too. Lines from other platforms are ignored.
/// Auto-discovered as an <see cref="IEventHandler{TEvent}"/>.
/// </summary>
public sealed class YouTubeVerifiedRecorder : IEventHandler<ChatMessageReceivedEvent>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public YouTubeVerifiedRecorder(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task HandleAsync(
        ChatMessageReceivedEvent @event,
        CancellationToken cancellationToken = default
    )
    {
        if (@event.Provider != AuthEnums.Platform.YouTube)
            return;

        using IServiceScope scope = _scopeFactory.CreateScope();
        IUserService users = scope.ServiceProvider.GetRequiredService<IUserService>();
        Result<UserDto> user = await users.GetOrCreateAsync(
            @event.UserId,
            @event.UserLogin,
            @event.UserDisplayName,
            @event.Provider,
            cancellationToken
        );
        if (user.IsFailure || !Guid.TryParse(user.Value.Id, out Guid userId))
            return;

        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        User? row = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (row is null || row.IsYouTubeChannelVerified == @event.IsYouTubeChannelVerified)
            return;

        row.IsYouTubeChannelVerified = @event.IsYouTubeChannelVerified;
        await db.SaveChangesAsync(cancellationToken);
    }
}
