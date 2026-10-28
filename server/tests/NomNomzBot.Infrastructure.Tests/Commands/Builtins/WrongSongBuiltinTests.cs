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

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NomNomzBot.Infrastructure.Music.PipelineActions;
using NomNomzBot.Infrastructure.Tests.Music;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// Stoney 2026-10-26: "why is wrongsong not a global command that works on every channel that has music
/// requests enabled?" It only existed as a hand-made command on two channels. As a built-in it reaches every
/// channel, and it runs the one song_wrong logic as the viewer who typed it.
/// </summary>
public sealed class WrongSongBuiltinTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-00000000ab08");

    private static MusicQueue QueueOfTwo() =>
        new(
            null,
            [
                new("My Pick", "A", null, 100, "Bamo", 0, "K7QM"),
                new("Their Pick", "B", null, 100, "SomeoneElse", 0, "P4XT"),
            ]
        );

    private static (WrongSongBuiltin Builtin, IMusicService Music) Build()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetQueueAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(QueueOfTwo());
        music
            .RetractRequestAsync(ChannelId.ToString(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(RequestRetraction.Removed);
        SongWrongAction action = new(
            music,
            MusicTestDbContext.New(),
            Substitute.For<IChatProvider>(),
            WrongSongTestComposer.Create(),
            NullLogger<SongWrongAction>.Instance
        );
        return (new WrongSongBuiltin([action]), music);
    }

    private static BuiltinCommandContext Bamo(string args) =>
        new()
        {
            BroadcasterId = ChannelId,
            TriggeringUserId = "twitch-42",
            TriggeringUserDisplayName = "Bamo",
            TriggeringUserLogin = "bamo",
            MessageId = "msg-1",
            Args = args,
        };

    [Fact]
    public void The_wrongsong_builtin_is_registered_so_every_channel_has_it()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Encryption:Key"] = Convert.ToBase64String(new byte[32]),
                    ["Jwt:Secret"] = "test-secret-key-at-least-32-characters-long!!",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=localhost;Database=wrongsong_registration_test;Username=test;Password=test",
                }
            )
            .Build();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddApplication();
        services.AddInfrastructure(configuration);

        List<Type?> builtinTypes =
        [
            .. services
                .Where(d => d.ServiceType == typeof(IBuiltinCommand))
                .Select(d => d.ImplementationType),
        ];

        builtinTypes.Should().Contain(typeof(WrongSongBuiltin));
        new WrongSongBuiltin([]).BuiltinKey.Should().Be("wrongsong");
    }

    [Fact]
    public async Task The_caller_removes_their_own_request_by_its_queue_number()
    {
        (WrongSongBuiltin builtin, IMusicService music) = Build();

        Result<string> reply = await builtin.ExecuteAsync(Bamo("1"));

        reply.IsSuccess.Should().BeTrue(reply.ErrorMessage);
        reply
            .Value.Should()
            .BeEmpty("song_wrong answers in chat itself, so the built-in adds no second line");
        await music
            .Received(1)
            .RetractRequestAsync(ChannelId.ToString(), 0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Someone_elses_request_is_never_removed()
    {
        (WrongSongBuiltin builtin, IMusicService music) = Build();

        await builtin.ExecuteAsync(Bamo("2"));

        await music.DidNotReceiveWithAnyArgs().RetractRequestAsync(default!, default);
    }
}
