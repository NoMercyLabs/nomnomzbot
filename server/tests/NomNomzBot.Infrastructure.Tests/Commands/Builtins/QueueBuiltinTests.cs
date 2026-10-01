// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

// Owner 2026-10-01: "!queue @f0xb17" and "!queue @Stoney_Eagle" both answered with the same top-five list,
// with no requester named anywhere.
public sealed class QueueBuiltinTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-000000009931");

    // 2 minutes of the playing track are left. f0xb17 holds #2 (after 3 min) and #4 (after 3 + 3:20 + 1:40).
    private static readonly NowPlaying Playing = new(
        TrackName: "Playing Now",
        Artist: "Someone",
        Album: null,
        ImageUrl: null,
        DurationMs: 200_000,
        ProgressMs: 80_000,
        IsPlaying: true,
        Volume: 50,
        RequestedBy: null,
        Provider: "spotify"
    );

    private static readonly IReadOnlyList<MusicQueueItem> Requests =
    [
        new("Song A", "Artist A", null, 180_000, "Stoney_Eagle"),
        new("Dr. Mabuse", "Propaganda", null, 200_000, "f0xb17"),
        new("Song C", "Artist C", null, 100_000, "Viewer3"),
        new("Song D", "Artist D", null, 240_000, "F0xB17"),
    ];

    private static BuiltinCommandContext Context(string args, string caller = "Lurker") =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeringUserId = "twitch-" + caller,
            TriggeringUserDisplayName = caller,
            TriggeringUserLogin = caller.ToLowerInvariant(),
            Args = args,
            Personality = PersonalityTone.Informative,
        };

    private static QueueBuiltin Sut(NowPlaying? playing, IReadOnlyList<MusicQueueItem> items)
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetQueueAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(new MusicQueue(playing, items));
        return new QueueBuiltin(music, FakeComposer());
    }

    private static IBuiltinResponseComposer FakeComposer()
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
                foreach (
                    KeyValuePair<string, string> kvp in call.ArgAt<IDictionary<string, string>>(1)
                )
                    template = template.Replace($"{{{kvp.Key}}}", kvp.Value);
                return Task.FromResult(template);
            });
        return new BuiltinResponseComposer(
            resolver,
            NoPlatformBuiltinReplies.Instance,
            FakeChannelBuiltinReplies.None
        );
    }

    [Fact]
    public async Task A_named_viewer_gets_only_their_own_requests_with_position_and_wait()
    {
        Result<string> result = await Sut(Playing, Requests).ExecuteAsync(Context("@f0xb17"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("f0xb17");
        result.Value.Should().Contain("#2 Dr. Mabuse by Propaganda (in ~5 min)");
        result.Value.Should().Contain("#4 Song D by Artist D (in ~10 min)");
        result.Value.Should().NotContain("Song A").And.NotContain("Song C");
    }

    [Fact]
    public async Task Two_different_names_get_two_different_answers()
    {
        QueueBuiltin sut = Sut(Playing, Requests);

        Result<string> fox = await sut.ExecuteAsync(Context("@f0xb17"));
        Result<string> stoney = await sut.ExecuteAsync(Context("@Stoney_Eagle"));

        stoney.Value.Should().Contain("#1 Song A by Artist A (in ~2 min)");
        stoney.Value.Should().NotContain("Dr. Mabuse");
        fox.Value.Should().NotContain("Song A");
    }

    [Fact]
    public async Task A_plain_queue_from_a_viewer_with_requests_answers_with_their_own()
    {
        Result<string> result = await Sut(Playing, Requests)
            .ExecuteAsync(Context(string.Empty, caller: "f0xb17"));

        result.Value.Should().Contain("#2 Dr. Mabuse by Propaganda (in ~5 min)");
        result.Value.Should().Contain("#4 Song D by Artist D");
        result.Value.Should().NotContain("Song C");
    }

    [Fact]
    public async Task A_plain_queue_from_a_viewer_without_requests_lists_the_queue_with_requesters()
    {
        Result<string> result = await Sut(Playing, Requests).ExecuteAsync(Context(string.Empty));

        result.Value.Should().Contain("1. Song A by Artist A (Stoney_Eagle)");
        result.Value.Should().Contain("2. Dr. Mabuse by Propaganda (f0xb17)");
        result.Value.Should().Contain("3. Song C by Artist C (Viewer3)");
    }

    [Fact]
    public async Task A_named_viewer_with_no_request_is_told_so_instead_of_seeing_the_list()
    {
        Result<string> result = await Sut(Playing, Requests).ExecuteAsync(Context("@nobody"));

        result.Value.Should().Contain("nobody");
        result.Value.Should().NotContain("Song A").And.NotContain("Dr. Mabuse");
    }

    [Fact]
    public async Task The_first_request_with_nothing_playing_is_up_next()
    {
        Result<string> result = await Sut(null, Requests).ExecuteAsync(Context("@Stoney_Eagle"));

        result.Value.Should().Contain("#1 Song A by Artist A (up next)");
    }

    [Fact]
    public async Task An_empty_queue_says_so()
    {
        Result<string> result = await Sut(Playing, []).ExecuteAsync(Context(string.Empty));

        result
            .Value.Should()
            .BeOneOf(
                ToneTemplateCatalog.Get(
                    PersonalityTone.Informative,
                    BuiltinResponseSlots.Queue.Key,
                    BuiltinResponseSlots.Queue.Empty
                )
            );
    }
}
