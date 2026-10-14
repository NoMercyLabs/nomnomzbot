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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat;
using DomainStream = NomNomzBot.Domain.Stream.Entities.Stream;

namespace NomNomzBot.Infrastructure.Tests.Chat;

public sealed class ChatColorAssignmentServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Channel = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly ChatColorTestDbContext _db = ChatColorTestDbContext.New();
    private readonly ServiceProvider _provider;
    private int _lineCounter;

    public ChatColorAssignmentServiceTests()
    {
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(_db);
        _provider = services.BuildServiceProvider();
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task Chatter_without_a_color_gets_the_color_no_chatter_used_in_the_last_14_days()
    {
        AddStream("recent", Now.AddDays(-2), null);
        AddStream("old", Now.AddDays(-21), Now.AddDays(-20));
        foreach (string color in ChatColorPalette.Colors.Where(c => c != "#00FF7F"))
            AddLines("recent", color, chatters: 1, linesEach: 1, Now.AddDays(-1));
        AddLines("old", "#00FF7F", chatters: 3, linesEach: 1, Now.AddDays(-20).AddHours(-1));
        await _db.SaveChangesAsync();

        string? assigned = await NewService().ResolveAsync(Channel, "twitch", "newbie", null);

        Assert.Equal("#00FF7F", assigned);
    }

    [Fact]
    public async Task Chatters_are_counted_once_however_many_lines_they_wrote()
    {
        AddStream("recent", Now.AddDays(-2), null);
        foreach (string color in ChatColorPalette.Colors.Where(c => c != "#FF69B4"))
            AddLines("recent", color, chatters: 2, linesEach: 1, Now.AddDays(-1));
        AddLines("recent", "#FF69B4", chatters: 1, linesEach: 5, Now.AddDays(-1));
        await _db.SaveChangesAsync();

        string? assigned = await NewService().ResolveAsync(Channel, "twitch", "newbie", null);

        Assert.Equal("#FF69B4", assigned);
    }

    [Fact]
    public async Task Chatter_keeps_the_assigned_color_on_later_lines_and_after_a_restart()
    {
        AddStream("recent", Now.AddDays(-2), null);
        foreach (string color in ChatColorPalette.Colors.Where(c => c != "#00FF7F"))
            AddLines("recent", color, chatters: 1, linesEach: 1, Now.AddDays(-1));
        await _db.SaveChangesAsync();

        string? first = await NewService().ResolveAsync(Channel, "twitch", "newbie", null);
        AddLines("recent", "#00FF7F", chatters: 4, linesEach: 1, Now.AddHours(-1));
        await _db.SaveChangesAsync();
        string? afterRestart = await NewService().ResolveAsync(Channel, "twitch", "newbie", null);

        Assert.Equal("#00FF7F", first);
        Assert.Equal(first, afterRestart);
        ChatterAssignedColor stored = await _db.ChatterAssignedColors.SingleAsync();
        Assert.Equal(
            (Channel, "twitch", "newbie", "#00FF7F"),
            (stored.BroadcasterId, stored.Provider, stored.UserId, stored.ColorHex)
        );
    }

    [Fact]
    public async Task A_chatter_own_color_wins_and_nothing_is_stored()
    {
        string? assigned = await NewService().ResolveAsync(Channel, "kick", "42", "#123456");

        Assert.Equal("#123456", assigned);
        Assert.Empty(await _db.ChatterAssignedColors.ToListAsync());
    }

    private ChatColorAssignmentService NewService() =>
        new(_provider.GetRequiredService<IServiceScopeFactory>(), new FakeTimeProvider(Now));

    private void AddStream(string id, DateTimeOffset startedAt, DateTimeOffset? endedAt) =>
        _db.Set<DomainStream>()
            .Add(
                new DomainStream
                {
                    Id = id,
                    ChannelId = Channel,
                    StartedAt = startedAt,
                    EndedAt = endedAt,
                }
            );

    private void AddLines(
        string streamId,
        string color,
        int chatters,
        int linesEach,
        DateTimeOffset at
    )
    {
        for (int chatter = 0; chatter < chatters; chatter++)
        for (int line = 0; line < linesEach; line++)
            _db.Set<ChatMessage>()
                .Add(
                    new ChatMessage
                    {
                        Id = $"m{_lineCounter++}",
                        BroadcasterId = Channel,
                        Provider = "twitch",
                        UserId = $"{streamId}-{color}-{chatter}",
                        Username = "u",
                        DisplayName = "u",
                        UserType = "viewer",
                        Message = "hi",
                        ColorHex = color,
                        StreamId = streamId,
                        CreatedAt = at.UtcDateTime,
                    }
                );
    }
}
