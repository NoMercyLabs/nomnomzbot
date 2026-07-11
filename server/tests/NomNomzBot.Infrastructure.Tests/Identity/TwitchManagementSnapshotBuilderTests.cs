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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the shared Twitch management snapshot builder (roles-permissions §4): it reads a channel's moderators
/// (badge) and editors (Helix), maps each to its resolved <c>User</c> Guid + role/source, and — critically for
/// the periodic reconcile's prune-safety — reports ONLY the sources whose read succeeded, so a failed read can
/// never authorize pruning that source's roles.
/// </summary>
public sealed class TwitchManagementSnapshotBuilderTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-00000000c001");
    private static readonly Guid ModGuid = Guid.Parse("0192a000-0000-7000-8000-00000000c010");
    private static readonly Guid EditorGuid = Guid.Parse("0192a000-0000-7000-8000-00000000c011");

    private static (
        TwitchManagementSnapshotBuilder Sut,
        ITwitchModeratorsApi Mods,
        ITwitchChannelsApi Channels,
        IUserService Users
    ) Build()
    {
        IUserService users = Substitute.For<IUserService>();
        ITwitchModeratorsApi mods = Substitute.For<ITwitchModeratorsApi>();
        ITwitchChannelsApi channels = Substitute.For<ITwitchChannelsApi>();
        users
            .GetOrCreateAsync(
                "tw-mod",
                "modlogin",
                "ModName",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(UserResult(ModGuid, "modlogin", "ModName"));
        users
            .GetOrCreateAsync(
                "tw-editor",
                "EditorName",
                "EditorName",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(UserResult(EditorGuid, "EditorName", "EditorName"));
        TwitchManagementSnapshotBuilder sut = new(
            users,
            mods,
            channels,
            NullLogger<TwitchManagementSnapshotBuilder>.Instance
        );
        return (sut, mods, channels, users);
    }

    [Fact]
    public async Task Builds_moderators_and_editors_with_both_sources_authoritative()
    {
        (
            TwitchManagementSnapshotBuilder sut,
            ITwitchModeratorsApi mods,
            ITwitchChannelsApi channels,
            _
        ) = Build();
        mods.GetModeratorsAsync(
                Broadcaster,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchModerator>(
                        [new TwitchModerator("tw-mod", "modlogin", "ModName")],
                        null,
                        1
                    )
                )
            );
        channels
            .GetChannelEditorsAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchChannelEditor>>([
                    new TwitchChannelEditor("tw-editor", "EditorName", DateTimeOffset.UtcNow),
                ])
            );

        ManagementSnapshot snapshot = await sut.BuildAsync(Broadcaster);

        snapshot
            .Members.Should()
            .BeEquivalentTo(
                new[]
                {
                    new TwitchManagementMember(
                        ModGuid,
                        "tw-mod",
                        ManagementRole.Moderator,
                        MembershipSource.TwitchBadge
                    ),
                    new TwitchManagementMember(
                        EditorGuid,
                        "tw-editor",
                        ManagementRole.Editor,
                        MembershipSource.HelixEditors
                    ),
                }
            );
        snapshot
            .AuthoritativeSources.Should()
            .BeEquivalentTo([MembershipSource.TwitchBadge, MembershipSource.HelixEditors]);
    }

    [Fact]
    public async Task A_failed_moderator_read_is_not_authoritative_and_yields_only_editors()
    {
        (
            TwitchManagementSnapshotBuilder sut,
            ITwitchModeratorsApi mods,
            ITwitchChannelsApi channels,
            _
        ) = Build();
        mods.GetModeratorsAsync(
                Broadcaster,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<TwitchPage<TwitchModerator>>(
                    "Missing required scope 'moderator:read:moderators'.",
                    TwitchErrorCodes.MissingScope
                )
            );
        channels
            .GetChannelEditorsAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchChannelEditor>>([
                    new TwitchChannelEditor("tw-editor", "EditorName", DateTimeOffset.UtcNow),
                ])
            );

        ManagementSnapshot snapshot = await sut.BuildAsync(Broadcaster);

        snapshot
            .Members.Should()
            .ContainSingle()
            .Which.Source.Should()
            .Be(MembershipSource.HelixEditors);
        // TwitchBadge read failed → it is NOT authoritative, so the reconciler must not prune badge rows.
        snapshot.AuthoritativeSources.Should().BeEquivalentTo([MembershipSource.HelixEditors]);
    }

    [Fact]
    public async Task A_user_who_is_both_mod_and_editor_is_recorded_once_at_the_higher_editor_role()
    {
        (
            TwitchManagementSnapshotBuilder sut,
            ITwitchModeratorsApi mods,
            ITwitchChannelsApi channels,
            IUserService users
        ) = Build();
        // Same Twitch id resolves to the same User for both the moderator and the editor entry.
        users
            .GetOrCreateAsync(
                "tw-both",
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(UserResult(ModGuid, "bothlogin", "BothName"));
        mods.GetModeratorsAsync(
                Broadcaster,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchModerator>(
                        [new TwitchModerator("tw-both", "bothlogin", "BothName")],
                        null,
                        1
                    )
                )
            );
        channels
            .GetChannelEditorsAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<TwitchChannelEditor>>([
                    new TwitchChannelEditor("tw-both", "BothName", DateTimeOffset.UtcNow),
                ])
            );

        ManagementSnapshot snapshot = await sut.BuildAsync(Broadcaster);

        snapshot.Members.Should().ContainSingle().Which.Role.Should().Be(ManagementRole.Editor);
    }

    [Fact]
    public async Task Paginates_all_moderator_pages_before_pruning()
    {
        (
            TwitchManagementSnapshotBuilder sut,
            ITwitchModeratorsApi mods,
            ITwitchChannelsApi channels,
            IUserService users
        ) = Build();
        Guid secondModGuid = Guid.Parse("0192a000-0000-7000-8000-00000000c012");
        users
            .GetOrCreateAsync(
                "tw-mod2",
                "mod2login",
                "Mod2Name",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(UserResult(secondModGuid, "mod2login", "Mod2Name"));

        // Page 1 carries a NextCursor; page 2 exhausts it. A single-page reader would drop mod2 and then
        // prune them — the exact churn this closes.
        mods.GetModeratorsAsync(
                Broadcaster,
                Arg.Is<TwitchPageRequest>(p => p.After == null),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchModerator>(
                        [new TwitchModerator("tw-mod", "modlogin", "ModName")],
                        "cursor-1",
                        2
                    )
                )
            );
        mods.GetModeratorsAsync(
                Broadcaster,
                Arg.Is<TwitchPageRequest>(p => p.After == "cursor-1"),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchModerator>(
                        [new TwitchModerator("tw-mod2", "mod2login", "Mod2Name")],
                        null,
                        2
                    )
                )
            );
        channels
            .GetChannelEditorsAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchChannelEditor>>([]));

        ManagementSnapshot snapshot = await sut.BuildAsync(Broadcaster);

        // Both mods across both pages are in the snapshot, and the source is authoritative (complete read).
        snapshot.Members.Select(m => m.UserId).Should().BeEquivalentTo([ModGuid, secondModGuid]);
        snapshot.AuthoritativeSources.Should().Contain(MembershipSource.TwitchBadge);
    }

    [Fact]
    public async Task An_unresolved_moderator_makes_the_snapshot_incomplete_and_not_authoritative()
    {
        (
            TwitchManagementSnapshotBuilder sut,
            ITwitchModeratorsApi mods,
            ITwitchChannelsApi channels,
            IUserService users
        ) = Build();
        // One mod resolves; a second mod's get-or-create fails (transient). The read SUCCEEDED but the
        // snapshot doesn't fully represent the channel — so TwitchBadge must NOT be authoritative for pruning,
        // or the reconcile would strip a real moderator's role (their dashboard goes blank).
        users
            .GetOrCreateAsync(
                "tw-flaky",
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<UserDto>("transient DB error", "SERVICE_UNAVAILABLE"));
        mods.GetModeratorsAsync(
                Broadcaster,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchModerator>(
                        [
                            new TwitchModerator("tw-mod", "modlogin", "ModName"),
                            new TwitchModerator("tw-flaky", "flakylogin", "FlakyName"),
                        ],
                        null,
                        2
                    )
                )
            );
        channels
            .GetChannelEditorsAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchChannelEditor>>([]));

        ManagementSnapshot snapshot = await sut.BuildAsync(Broadcaster);

        // The resolved mod is still recorded (so inserts work), but pruning is disabled this run.
        snapshot.Members.Should().ContainSingle().Which.UserId.Should().Be(ModGuid);
        snapshot.AuthoritativeSources.Should().NotContain(MembershipSource.TwitchBadge);
    }

    [Fact]
    public async Task A_failed_second_page_disables_pruning_even_though_page_one_succeeded()
    {
        (
            TwitchManagementSnapshotBuilder sut,
            ITwitchModeratorsApi mods,
            ITwitchChannelsApi channels,
            _
        ) = Build();
        mods.GetModeratorsAsync(
                Broadcaster,
                Arg.Is<TwitchPageRequest>(p => p.After == null),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchModerator>(
                        [new TwitchModerator("tw-mod", "modlogin", "ModName")],
                        "cursor-1",
                        2
                    )
                )
            );
        mods.GetModeratorsAsync(
                Broadcaster,
                Arg.Is<TwitchPageRequest>(p => p.After == "cursor-1"),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<TwitchPage<TwitchModerator>>(
                    "Twitch 503",
                    TwitchErrorCodes.TwitchError
                )
            );
        channels
            .GetChannelEditorsAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchChannelEditor>>([]));

        ManagementSnapshot snapshot = await sut.BuildAsync(Broadcaster);

        snapshot.AuthoritativeSources.Should().NotContain(MembershipSource.TwitchBadge);
    }

    private static Result<UserDto> UserResult(Guid id, string username, string displayName) =>
        Result.Success(
            new UserDto(
                id.ToString(),
                username,
                displayName,
                null,
                null,
                DateTime.UtcNow,
                DateTime.UtcNow
            )
        );
}
