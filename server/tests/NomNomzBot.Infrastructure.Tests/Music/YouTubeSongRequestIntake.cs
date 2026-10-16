// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// A YouTube-only channel end to end: the real <c>!sr</c> builtin and its shipped reply catalogue, over the
/// real <see cref="MusicService"/>, over the real <see cref="YouTubeMusicProvider"/>, over a recorded HTTP
/// handler. A test reads the exact chat reply, the queue that resulted, and every Data API call made.
/// </summary>
internal sealed class YouTubeSongRequestIntake
{
    public static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-00000000f7a1");

    private readonly SongRequestBuiltin _builtin;

    public YouTubeSongRequestIntake(MusicConfigDto? config = null)
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        db.Services.Add(
            new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = "youtube",
                BroadcasterId = ChannelId,
                Enabled = true,
                AccessToken = "youtube-connected",
            }
        );
        db.SaveChanges();

        Provider = YouTubeProviderFactory.Create("test-key", Http, db);
        Music = new MusicService(
            [Provider],
            db,
            new RecordingEventBus(),
            new BlockedTrackService(db),
            new SongRequestQueueStore(),
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            Config(config),
            Substitute.For<ICurrencyAccountService>(),
            new NowPlayingCache(),
            new OutboundSanctionAccessor(),
            Substitute.For<IUserIdentityService>(),
            Substitute.For<IForeignLinkTitleLookup>()
        );
        _builtin = new(Music, RealComposer(), Substitute.For<IEventBus>());
    }

    public RecordingHttpHandler Http { get; } = new();

    public YouTubeMusicProvider Provider { get; }

    public MusicService Music { get; }

    /// <summary>Data API calls made so far, by endpoint name ("search", "videos").</summary>
    public List<string> DataApiCalls =>
        Http
            .RequestUrls.Where(url => url.Contains("googleapis.com", StringComparison.Ordinal))
            .Select(url => new Uri(url[(url.IndexOf(' ') + 1)..]).AbsolutePath.Split('/')[^1])
            .ToList();

    /// <summary>Sends <c>!sr {args}</c> as a viewer (or the given role) and returns the chat reply.</summary>
    public async Task<string> SayAsync(string args, int roleLevel = 0)
    {
        Result<string> reply = await _builtin.ExecuteAsync(
            new()
            {
                BroadcasterId = ChannelId,
                TriggeringUserId = "viewer-1",
                TriggeringUserDisplayName = "Viewer",
                TriggeringUserLogin = "viewer",
                RoleLevel = roleLevel,
                Args = args,
                Personality = PersonalityTone.Informative,
                MessageId = "msg-1",
            }
        );
        return reply.Value;
    }

    public async Task<List<string>> QueuedTitlesAsync() =>
        (await Music.GetQueueAsync(ChannelId.ToString())).Queue.Select(i => i.TrackName).ToList();

    public void RouteVideos(params string[] videos) =>
        Http.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/videos", StringComparison.Ordinal),
            HttpStatusCode.OK,
            "{\"items\":[" + string.Join(",", videos) + "]}"
        );

    public void RouteSearch(params string[] videoIds) =>
        Http.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/search", StringComparison.Ordinal),
            HttpStatusCode.OK,
            "{\"items\":["
                + string.Join(",", videoIds.Select(id => "{\"id\":{\"videoId\":\"" + id + "\"}}"))
                + "]}"
        );

    /// <summary>One videos.list item. <paramref name="extraContentDetails"/> is raw JSON appended inside
    /// <c>contentDetails</c> (e.g. a contentRating or regionRestriction).</summary>
    public static string Video(
        string id,
        string title,
        string channel = "Some Channel",
        string? duration = "PT3M30S",
        bool embeddable = true,
        string live = "none",
        string extraContentDetails = ""
    )
    {
        List<string> details = [];
        if (duration is not null)
            details.Add("\"duration\":\"" + duration + "\"");
        if (extraContentDetails.Length > 0)
            details.Add(extraContentDetails);

        return "{\"id\":\""
            + id
            + "\",\"snippet\":{\"title\":\""
            + title
            + "\",\"channelTitle\":\""
            + channel
            + "\",\"liveBroadcastContent\":\""
            + live
            + "\",\"thumbnails\":{}},\"contentDetails\":{"
            + string.Join(",", details)
            + "},\"status\":{\"embeddable\":"
            + (embeddable ? "true" : "false")
            + "}}";
    }

    private static IMusicConfigService Config(MusicConfigDto? config)
    {
        if (config is null)
            return PermissiveMusicConfigService.Instance;

        IMusicConfigService service = Substitute.For<IMusicConfigService>();
        service
            .GetConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(config));
        return service;
    }

    /// <summary>The shipped catalogue through the real composer, with a plain {variable} substitution.</summary>
    private static BuiltinResponseComposer RealComposer()
    {
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                string template = call.ArgAt<string>(0);
                IDictionary<string, string> variables = call.ArgAt<IDictionary<string, string>>(1);
                foreach (KeyValuePair<string, string> variable in variables)
                    template = template.Replace($"{{{variable.Key}}}", variable.Value);
                return Task.FromResult(template);
            });

        return new(resolver, NoPlatformBuiltinReplies.Instance, FakeChannelBuiltinReplies.None);
    }
}
