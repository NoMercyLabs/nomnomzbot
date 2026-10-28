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
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Dtos;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// The owner's own chat identity: the color Twitch holds, the named palette, whether the color can be
/// changed, the badges worn on the owner's latest line, and the plain refusal Twitch gives a hex color
/// for a user without Turbo or Prime.
/// </summary>
public sealed class ChatIdentityServiceTests : IAsyncDisposable
{
    private static readonly Guid Broadcaster = Guid.Parse("0197b2c0-0000-7000-8000-0000000000aa");
    private const string OwnerTwitchId = "1234";

    private readonly ChatColorTestDbContext _db = ChatColorTestDbContext.New();
    private readonly ITwitchChatApi _chat = Substitute.For<ITwitchChatApi>();
    private readonly ITwitchTokenResolver _tokens = Substitute.For<ITwitchTokenResolver>();
    private readonly IChatBadgeResolver _badges = Substitute.For<IChatBadgeResolver>();
    private readonly ChatIdentityService _service;

    public ChatIdentityServiceTests()
    {
        ITwitchHelixClient helix = Substitute.For<ITwitchHelixClient>();
        helix.Chat.Returns(_chat);
        _chat
            .GetUserChatColorAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TwitchUserChatColor(OwnerTwitchId, "stoney", "Stoney", "#FF69B4")
                )
            );
        _tokens
            .HasScopeAsync(
                Broadcaster,
                TwitchScopes.UserManageChatColor,
                Arg.Any<CancellationToken>()
            )
            .Returns(true);
        _badges
            .ResolveAsync(
                Broadcaster,
                Arg.Any<IReadOnlyList<ChatBadge>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                call.Arg<IReadOnlyList<ChatBadge>>()
                    .Select(b => new ResolvedChatBadge(
                        b.SetId,
                        b.Id,
                        b.Info,
                        new Dictionary<string, string> { ["1"] = $"https://badge/{b.SetId}/1" }
                    ))
                    .ToList()
            );
        _service = new(_db, helix, _tokens, _badges);
    }

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    [Fact]
    public async Task Get_returns_the_color_the_palette_and_the_badges_of_the_owners_latest_line()
    {
        await AddLineAsync("old", [new("premium", "1")], DateTime.UtcNow.AddMinutes(-10));
        await AddLineAsync(
            "new",
            [new("broadcaster", "1"), new("subscriber", "12", "14")],
            DateTime.UtcNow
        );

        Result<ChatIdentityDto> result = await _service.GetAsync(Broadcaster);

        result.IsSuccess.Should().BeTrue();
        ChatIdentityDto identity = result.Value;
        identity.DisplayName.Should().Be("Stoney");
        identity.Color.Should().Be("#FF69B4");
        identity.CanManageColor.Should().BeTrue();
        identity.ColorScope.Should().Be("user:manage:chat_color");
        identity.NamedColors.Should().HaveCount(15);
        identity.NamedColors[0].Should().Be(new ChatNamedColorDto("blue", "#0000FF"));
        identity.NamedColors.Should().Contain(new ChatNamedColorDto("hot_pink", "#FF69B4"));
        identity.Badges.Select(b => b.SetId).Should().Equal("broadcaster", "subscriber");
        identity.Badges[1].Info.Should().Be("14");
        identity.Badges[0].Urls["1"].Should().Be("https://badge/broadcaster/1");
    }

    [Fact]
    public async Task Get_reports_no_color_and_no_scope_plainly()
    {
        _chat
            .GetUserChatColorAsync(Broadcaster, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(new TwitchUserChatColor(OwnerTwitchId, "stoney", "Stoney", ""))
            );
        _tokens
            .HasScopeAsync(
                Broadcaster,
                TwitchScopes.UserManageChatColor,
                Arg.Any<CancellationToken>()
            )
            .Returns(false);

        ChatIdentityDto identity = (await _service.GetAsync(Broadcaster)).Value;

        identity.Color.Should().BeNull();
        identity.CanManageColor.Should().BeFalse();
        identity.Badges.Should().BeEmpty();
    }

    [Fact]
    public async Task Set_sends_a_named_color_to_twitch_and_returns_the_new_identity()
    {
        _chat
            .UpdateUserChatColorAsync(Broadcaster, "hot_pink", Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        Result<ChatIdentityDto> result = await _service.SetColorAsync(Broadcaster, "Hot_Pink");

        result.IsSuccess.Should().BeTrue();
        result.Value.Color.Should().Be("#FF69B4");
        await _chat
            .Received(1)
            .UpdateUserChatColorAsync(Broadcaster, "hot_pink", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Set_refuses_a_value_that_is_neither_a_name_nor_a_hex_without_calling_twitch()
    {
        Result<ChatIdentityDto> result = await _service.SetColorAsync(Broadcaster, "pinkish");

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        await _chat.DidNotReceiveWithAnyArgs().UpdateUserChatColorAsync(default, default!, default);
    }

    [Fact]
    public async Task Set_turns_the_twitch_turbo_refusal_into_a_plain_typed_error()
    {
        _chat
            .UpdateUserChatColorAsync(Broadcaster, "#9146FF", Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure(
                    "Twitch request failed (400).",
                    TwitchErrorCodes.TwitchError,
                    "To specify a Hex color code, the user must be a Turbo or Prime user."
                )
            );

        Result<ChatIdentityDto> result = await _service.SetColorAsync(Broadcaster, "#9146ff");

        result.ErrorCode.Should().Be(IChatIdentityService.HexNeedsTurboCode);
        result.ErrorMessage.Should().Contain("Turbo or Prime");
    }

    [Fact]
    public async Task Set_passes_a_missing_scope_through()
    {
        _chat
            .UpdateUserChatColorAsync(Broadcaster, "red", Arg.Any<CancellationToken>())
            .Returns(Result.Failure("Missing required scope.", TwitchErrorCodes.MissingScope));

        Result<ChatIdentityDto> result = await _service.SetColorAsync(Broadcaster, "red");

        result.ErrorCode.Should().Be(TwitchErrorCodes.MissingScope);
    }

    [Fact]
    public void The_color_scope_is_a_named_feature_so_the_additive_regrant_requests_it()
    {
        FeatureScopeMap.FeatureForScope(TwitchScopes.UserManageChatColor).Should().Be("chat_color");
    }

    private async Task AddLineAsync(string id, List<ChatBadge> badges, DateTime at)
    {
        _db.ChatMessages.Add(
            new ChatMessage
            {
                Id = id,
                BroadcasterId = Broadcaster,
                UserId = OwnerTwitchId,
                Username = "stoney",
                DisplayName = "Stoney",
                UserType = "broadcaster",
                Message = "hi",
                Badges = badges,
                CreatedAt = at,
            }
        );
        await _db.SaveChangesAsync();
    }
}
