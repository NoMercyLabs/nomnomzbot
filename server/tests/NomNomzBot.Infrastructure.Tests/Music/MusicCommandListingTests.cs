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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// <c>!commands</c> must list only what the caller can really run. <c>!bansong</c> refuses everyone without
/// the music:queue:moderate grant, so it is hidden from a VIP without it; <c>!volume</c> (anyone may read)
/// and <c>!skip</c> (viewers skip their own song) stay listed. <c>!volume &lt;n&gt;</c> stays gated.
/// </summary>
public sealed class MusicCommandListingTests
{
    private const int VipLevel = 4;
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000ac001");

    private static BuiltinCommandContext Ctx(string args = "", int roleLevel = VipLevel) =>
        new()
        {
            BroadcasterId = Broadcaster,
            TriggeringUserId = "twitch-42",
            TriggeringUserLogin = "vipuser",
            TriggeringUserDisplayName = "VipUser",
            Args = args,
            RoleLevel = roleLevel,
        };

    private static IBuiltinResponseComposer Composer()
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

    private static async Task<string> ListingForAsync(bool holdsGrant, int roleLevel = VipLevel)
    {
        MusicModerationGate gate = MusicGateTestKit.Gate(holdsGrant);
        IMusicService music = Substitute.For<IMusicService>();
        IBuiltinResponseComposer composer = Composer();
        List<IBuiltinCommand> impls =
        [
            new VolumeBuiltin(music, composer, gate),
            new SkipBuiltin(music, composer, gate),
            new BanSongBuiltin(music, Substitute.For<IBlockedTrackService>(), composer, gate),
        ];

        IBuiltinCommandCatalog catalog = Substitute.For<IBuiltinCommandCatalog>();
        catalog
            .Get(Arg.Any<string>())
            .Returns(call => impls.Find(i => i.BuiltinKey == call.Arg<string>()));

        IBuiltinCommandService builtins = Substitute.For<IBuiltinCommandService>();
        builtins
            .ListAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<BuiltinCommandDto>>([
                    .. impls.Select(i => new BuiltinCommandDto(
                        i.BuiltinKey,
                        i.BuiltinKey,
                        true,
                        5,
                        0,
                        "Everyone",
                        i.BuiltinKey,
                        false,
                        false,
                        false,
                        null,
                        null,
                        null,
                        0
                    )),
                ])
            );

        IServiceProvider provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IBuiltinCommandService)).Returns(builtins);
        provider.GetService(typeof(IBuiltinCommandCatalog)).Returns(catalog);

        ICommandService commands = Substitute.For<ICommandService>();
        commands
            .ListAsync(Arg.Any<string>(), Arg.Any<PaginationParams>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new PagedList<CommandListItem>([], 1, 100, 0)));

        CommandsBuiltin commandsBuiltin = new(commands, provider, composer);
        Result<string> result = await commandsBuiltin.ExecuteAsync(Ctx(roleLevel: roleLevel));
        return result.Value;
    }

    [Fact]
    public async Task A_vip_without_the_grant_does_not_see_bansong_but_still_sees_volume_and_skip()
    {
        string listing = await ListingForAsync(holdsGrant: false);

        listing.Should().NotContain("!bansong");
        listing.Should().Contain("!volume");
        listing.Should().Contain("!skip");
    }

    [Fact]
    public async Task A_caller_with_the_grant_sees_bansong()
    {
        string listing = await ListingForAsync(holdsGrant: true);

        listing.Should().Contain("!bansong");
        listing.Should().Contain("!volume");
        listing.Should().Contain("!skip");
    }

    [Fact]
    public async Task A_vip_without_the_grant_can_read_the_volume()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new NowPlaying(
                    "Song",
                    "Artist",
                    null,
                    null,
                    180_000,
                    1_000,
                    true,
                    37,
                    null,
                    "spotify"
                )
            );
        VolumeBuiltin sut = new(music, Composer(), MusicGateTestKit.Gate(false));

        Result<string> result = await sut.ExecuteAsync(Ctx());

        result.Value.Should().Contain("37");
    }

    [Fact]
    public async Task A_vip_without_the_grant_is_refused_setting_the_volume_and_it_does_not_change()
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(Broadcaster.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new NowPlaying(
                    "Song",
                    "Artist",
                    null,
                    null,
                    180_000,
                    1_000,
                    true,
                    37,
                    null,
                    "spotify"
                )
            );
        VolumeBuiltin sut = new(music, Composer(), MusicGateTestKit.Gate(false));

        Result<string> result = await sut.ExecuteAsync(Ctx("80"));

        result.Value.Should().NotContain("80");
        await music.DidNotReceiveWithAnyArgs().SetVolumeAsync(default!, default, default);
    }
}
