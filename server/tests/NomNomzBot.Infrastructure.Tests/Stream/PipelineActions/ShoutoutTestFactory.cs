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
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Stream;
using NomNomzBot.Infrastructure.Stream.PipelineActions;

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
        IShoutoutQueue? queue = null
    ) =>
        new(
            users,
            registry,
            queue ?? new ShoutoutQueue(),
            Sender(chat, registry, db, tts, time),
            resolver,
            time,
            NullLogger<ShoutoutAction>.Instance
        );

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
