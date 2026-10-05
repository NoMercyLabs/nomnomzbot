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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NomNomzBot.Infrastructure.Stream;
using NomNomzBot.Infrastructure.Stream.PipelineActions;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream.PipelineActions;

/// <summary>Wires the real shoutout action, sender, queue and worker around substituted Twitch seams.</summary>
internal static class ShoutoutTestFactory
{
    public static ShoutoutAction Create(
        ITwitchChatApi chat,
        ITwitchUsersApi users,
        IChannelRegistry registry,
        IApplicationDbContext db,
        ITemplateResolver resolver,
        ITtsDispatchService tts,
        TimeProvider time,
        IShoutoutQueue? queue = null,
        ITwitchChannelsApi? channels = null
    ) =>
        new(
            users,
            channels ?? NoChannelInfo(),
            registry,
            queue ?? new ShoutoutQueue(),
            Sender(chat, registry, db, tts, time),
            resolver,
            Composer(resolver),
            time,
            NullLogger<ShoutoutAction>.Instance
        );

    /// <summary>A channels client whose category lookup fails — the shoutout then falls back to its default game text.</summary>
    public static ITwitchChannelsApi NoChannelInfo()
    {
        ITwitchChannelsApi channels = Substitute.For<ITwitchChannelsApi>();
        channels
            .GetChannelInformationByTwitchIdsAsync(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Task.FromResult(
                    Result.Failure<IReadOnlyList<TwitchChannelInformation>>(
                        "no info",
                        TwitchErrorCodes.TwitchError
                    )
                )
            );
        return channels;
    }

    /// <summary>The real composer over the given resolver, with no platform text and no channel override set.</summary>
    public static BuiltinResponseComposer Composer(ITemplateResolver resolver)
    {
        IPlatformBuiltinReplyDefaults platform = Substitute.For<IPlatformBuiltinReplyDefaults>();
        platform
            .GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));
        return new(resolver, platform, FakeChannelBuiltinReplies.None);
    }

    public static ShoutoutSender Sender(
        ITwitchChatApi chat,
        IChannelRegistry registry,
        IApplicationDbContext db,
        ITtsDispatchService tts,
        TimeProvider time
    ) => new(chat, registry, db, tts, time, NullLogger<ShoutoutSender>.Instance);

    public static ShoutoutQueueWorker Worker(
        IShoutoutQueue queue,
        ITwitchChatApi chat,
        IChannelRegistry registry,
        IApplicationDbContext db,
        ITtsDispatchService tts,
        TimeProvider time
    )
    {
        ServiceCollection services = new();
        services.AddScoped<IShoutoutSender>(_ => Sender(chat, registry, db, tts, time));
        ServiceProvider provider = services.BuildServiceProvider();
        return new(
            queue,
            registry,
            provider.GetRequiredService<IServiceScopeFactory>(),
            time,
            NullLogger<ShoutoutQueueWorker>.Instance
        );
    }
}
