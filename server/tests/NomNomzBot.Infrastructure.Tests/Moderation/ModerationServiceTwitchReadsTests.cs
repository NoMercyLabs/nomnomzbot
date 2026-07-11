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
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Moderation;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Proves the moderation page reflects and controls REAL Twitch state rather than a local mirror. Before this
/// fix the banned-users list read only the bans the bot itself recorded, and the blocked-terms + Shield-Mode
/// controls read and wrote a local config Twitch never saw (cosmetic switches). Every test asserts the exact
/// Helix call made and the shape of what is returned — and that a Helix failure surfaces as an honest error,
/// never a silently-empty success.
/// </summary>
public sealed class ModerationServiceTwitchReadsTests
{
    private static readonly Guid Tenant = Guid.Parse("019f2802-5c77-7dc8-b6f6-b4b98e624b8a");
    private static string BroadcasterId => Tenant.ToString();

    private static ModerationService NewService(ITwitchModerationApi moderation) =>
        new(
            ModerationServiceTestDbContext.New(),
            moderation,
            NullLogger<ModerationService>.Instance,
            Substitute.For<IEventBus>()
        );

    private static TwitchBannedUser Banned(
        string id,
        string name,
        string reason,
        string moderatorName,
        DateTimeOffset? expiresAt
    ) =>
        new(
            UserId: id,
            UserLogin: name.ToLowerInvariant(),
            UserName: name,
            ExpiresAt: expiresAt,
            CreatedAt: new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero),
            Reason: reason,
            ModeratorId: "9000",
            ModeratorLogin: moderatorName.ToLowerInvariant(),
            ModeratorName: moderatorName
        );

    private static TwitchBlockedTerm Term(string id, string text) =>
        new(
            BroadcasterId: "1001",
            ModeratorId: "1001",
            Id: id,
            Text: text,
            CreatedAt: DateTimeOffset.UnixEpoch,
            UpdatedAt: DateTimeOffset.UnixEpoch,
            ExpiresAt: null
        );

    private static TwitchUnbanRequest Unban(
        string id,
        string userName,
        string status,
        string moderatorName = "",
        string resolutionText = ""
    ) =>
        new(
            Id: id,
            BroadcasterId: "1001",
            BroadcasterLogin: "owner",
            BroadcasterName: "Owner",
            ModeratorId: moderatorName == "" ? "" : "9000",
            ModeratorLogin: moderatorName.ToLowerInvariant(),
            ModeratorName: moderatorName,
            UserId: "500" + id,
            UserLogin: userName.ToLowerInvariant(),
            UserName: userName,
            Text: "please unban me",
            Status: status,
            CreatedAt: new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero),
            ResolvedAt: null,
            ResolutionText: resolutionText
        );

    // ─── Banned users ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetBannedUsersAsync_MapsPermanentBansFromTwitch_AndExcludesActiveTimeouts()
    {
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .GetBannedUsersAsync(Tenant, Arg.Any<TwitchPageRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchBannedUser>(
                        [
                            Banned("100", "Griefer", "spam", "ModAlice", expiresAt: null),
                            // An ACTIVE timeout — must NOT appear in the permanent banned-users list.
                            Banned(
                                "200",
                                "Loud",
                                "caps",
                                "ModBob",
                                expiresAt: new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)
                            ),
                        ],
                        NextCursor: null,
                        Total: 2
                    )
                )
            );

        Result<List<BannedUserDto>> result = await NewService(moderation)
            .GetBannedUsersAsync(BroadcasterId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        BannedUserDto ban = result.Value.Single();
        ban.UserId.Should().Be("100");
        ban.Username.Should().Be("Griefer");
        ban.Reason.Should().Be("spam");
        ban.BannedBy.Should().Be("ModAlice"); // the REAL moderator from Twitch, not the bot
    }

    [Fact]
    public async Task GetBannedUsersAsync_FollowsTheCursorAcrossEveryPage()
    {
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .GetBannedUsersAsync(Tenant, Arg.Any<TwitchPageRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                string? after = call.Arg<TwitchPageRequest>().After;
                return after is null
                    ? Result.Success(
                        new TwitchPage<TwitchBannedUser>(
                            [Banned("1", "A", "r", "M", expiresAt: null)],
                            NextCursor: "page2",
                            Total: 2
                        )
                    )
                    : Result.Success(
                        new TwitchPage<TwitchBannedUser>(
                            [Banned("2", "B", "r", "M", expiresAt: null)],
                            NextCursor: null,
                            Total: 2
                        )
                    );
            });

        Result<List<BannedUserDto>> result = await NewService(moderation)
            .GetBannedUsersAsync(BroadcasterId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(b => b.UserId).Should().BeEquivalentTo(["1", "2"]);
    }

    [Fact]
    public async Task GetBannedUsersAsync_WhenTwitchFails_SurfacesTheErrorNotAnEmptyList()
    {
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .GetBannedUsersAsync(Tenant, Arg.Any<TwitchPageRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<TwitchPage<TwitchBannedUser>>("Missing scope.", "missing_scope")
            );

        Result<List<BannedUserDto>> result = await NewService(moderation)
            .GetBannedUsersAsync(BroadcasterId);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("missing_scope");
    }

    // ─── Blocked terms ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetBlockedTermsAsync_ReturnsTheTermTextsFromTwitch()
    {
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .GetBlockedTermsAsync(
                Tenant,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchBlockedTerm>(
                        [Term("t1", "badword"), Term("t2", "worse*")],
                        NextCursor: null,
                        Total: 2
                    )
                )
            );

        Result<List<string>> result = await NewService(moderation)
            .GetBlockedTermsAsync(BroadcasterId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(["badword", "worse*"]);
    }

    [Fact]
    public async Task AddBlockedTermAsync_AddsOnTwitch_ThenReturnsTheRefreshedList()
    {
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .AddBlockedTermAsync(Tenant, "newterm", Arg.Any<CancellationToken>())
            .Returns(Result.Success(Term("t9", "newterm")));
        moderation
            .GetBlockedTermsAsync(
                Tenant,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchBlockedTerm>(
                        [Term("t9", "newterm")],
                        NextCursor: null,
                        Total: 1
                    )
                )
            );

        Result<List<string>> result = await NewService(moderation)
            .AddBlockedTermAsync(BroadcasterId, "  newterm  ");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Should().Be("newterm");
        // The term reached Twitch (trimmed), not a local store.
        await moderation
            .Received(1)
            .AddBlockedTermAsync(Tenant, "newterm", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveBlockedTermAsync_ResolvesTheTextToItsId_AndDeletesByIdOnTwitch()
    {
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .GetBlockedTermsAsync(
                Tenant,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchBlockedTerm>(
                        [Term("keep-id", "keeper"), Term("kill-id", "badword")],
                        NextCursor: null,
                        Total: 2
                    )
                )
            );
        moderation
            .RemoveBlockedTermAsync(Tenant, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        Result<List<string>> result = await NewService(moderation)
            .RemoveBlockedTermAsync(BroadcasterId, "BADWORD"); // case-insensitive match

        result.IsSuccess.Should().BeTrue();
        // Deleted the matching term BY ITS TWITCH ID, and left the other alone.
        await moderation
            .Received(1)
            .RemoveBlockedTermAsync(Tenant, "kill-id", Arg.Any<CancellationToken>());
        await moderation
            .DidNotReceive()
            .RemoveBlockedTermAsync(Tenant, "keep-id", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveBlockedTermAsync_WhenTermIsAbsent_IsIdempotentAndCallsNoDelete()
    {
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .GetBlockedTermsAsync(
                Tenant,
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchBlockedTerm>(
                        [Term("keep-id", "keeper")],
                        NextCursor: null,
                        Total: 1
                    )
                )
            );

        Result<List<string>> result = await NewService(moderation)
            .RemoveBlockedTermAsync(BroadcasterId, "not-present");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(["keeper"]);
        await moderation
            .DidNotReceive()
            .RemoveBlockedTermAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    // ─── Shield Mode ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetShieldModeAsync_ReportsTwitchsActivationState()
    {
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .GetShieldModeStatusAsync(Tenant, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchShieldModeStatus(
                        IsActive: true,
                        ModeratorId: "1001",
                        ModeratorLogin: "owner",
                        ModeratorName: "Owner",
                        LastActivatedAt: DateTimeOffset.UnixEpoch
                    )
                )
            );

        Result<bool> result = await NewService(moderation).GetShieldModeAsync(BroadcasterId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task SetShieldModeAsync_TogglesOnTwitch_AndReturnsTheAppliedState()
    {
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .UpdateShieldModeStatusAsync(Tenant, true, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchShieldModeStatus(
                        IsActive: true,
                        ModeratorId: "1001",
                        ModeratorLogin: "owner",
                        ModeratorName: "Owner",
                        LastActivatedAt: DateTimeOffset.UnixEpoch
                    )
                )
            );

        Result<bool> result = await NewService(moderation).SetShieldModeAsync(BroadcasterId, true);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
        // The toggle actually armed Shield Mode ON TWITCH — not a local flag.
        await moderation
            .Received(1)
            .UpdateShieldModeStatusAsync(Tenant, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetShieldModeAsync_WhenTwitchFails_SurfacesTheError()
    {
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .UpdateShieldModeStatusAsync(Tenant, true, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<TwitchShieldModeStatus>("Missing scope.", "missing_scope"));

        Result<bool> result = await NewService(moderation).SetShieldModeAsync(BroadcasterId, true);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("missing_scope");
    }

    // ─── Unban requests ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetUnbanRequestsAsync_MapsThePendingQueueFromTwitch()
    {
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .GetUnbanRequestsAsync(
                Tenant,
                "pending",
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new TwitchPage<TwitchUnbanRequest>(
                        [Unban("1", "Appealer", "pending"), Unban("2", "Sorry", "pending")],
                        NextCursor: null,
                        Total: 2
                    )
                )
            );

        Result<List<UnbanRequestDto>> result = await NewService(moderation)
            .GetUnbanRequestsAsync(BroadcasterId, "pending");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        UnbanRequestDto first = result.Value[0];
        first.Id.Should().Be("1");
        first.UserName.Should().Be("Appealer");
        first.Status.Should().Be("pending");
        first.Text.Should().Be("please unban me");
        first.ResolvedBy.Should().BeNull(); // unresolved → no moderator/note yet
    }

    [Fact]
    public async Task GetUnbanRequestsAsync_RejectsAnUnknownStatus_WithoutCallingTwitch()
    {
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();

        Result<List<UnbanRequestDto>> result = await NewService(moderation)
            .GetUnbanRequestsAsync(BroadcasterId, "bogus");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        await moderation
            .DidNotReceive()
            .GetUnbanRequestsAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ResolveUnbanRequestAsync_Approve_SendsApprovedToTwitch_AndReturnsResolved()
    {
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .ResolveUnbanRequestAsync(
                Tenant,
                "req-7",
                "approved",
                "welcome back",
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    Unban(
                        "7",
                        "Forgiven",
                        "approved",
                        moderatorName: "ModAlice",
                        resolutionText: "welcome back"
                    )
                )
            );

        Result<UnbanRequestDto> result = await NewService(moderation)
            .ResolveUnbanRequestAsync(BroadcasterId, "req-7", approve: true, note: "welcome back");

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("approved");
        result.Value.ResolvedBy.Should().Be("ModAlice");
        result.Value.ResolutionText.Should().Be("welcome back");
        // The APPROVED status reached Twitch (not a local flag).
        await moderation
            .Received(1)
            .ResolveUnbanRequestAsync(
                Tenant,
                "req-7",
                "approved",
                "welcome back",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ResolveUnbanRequestAsync_Deny_SendsDeniedToTwitch()
    {
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .ResolveUnbanRequestAsync(Tenant, "req-9", "denied", null, Arg.Any<CancellationToken>())
            .Returns(Result.Success(Unban("9", "Nope", "denied", moderatorName: "ModBob")));

        Result<UnbanRequestDto> result = await NewService(moderation)
            .ResolveUnbanRequestAsync(BroadcasterId, "req-9", approve: false, note: null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("denied");
        await moderation
            .Received(1)
            .ResolveUnbanRequestAsync(
                Tenant,
                "req-9",
                "denied",
                null,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ResolveUnbanRequestAsync_WhenTwitchFails_SurfacesTheError()
    {
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .ResolveUnbanRequestAsync(
                Tenant,
                "req-1",
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<TwitchUnbanRequest>("Missing scope.", "missing_scope"));

        Result<UnbanRequestDto> result = await NewService(moderation)
            .ResolveUnbanRequestAsync(BroadcasterId, "req-1", approve: true, note: null);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("missing_scope");
    }
}
