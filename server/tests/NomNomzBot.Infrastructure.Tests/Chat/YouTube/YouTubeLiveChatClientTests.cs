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
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Infrastructure.Chat.YouTube;

namespace NomNomzBot.Infrastructure.Tests.Chat.YouTube;

/// <summary>
/// Proves the YouTube live-chat READ transport (combined-chat item 6) maps the Data API wire shape correctly:
/// the active broadcast resolves to its <c>liveChatId</c>; not-being-live is a success with a null value (not an
/// error); each message flattens to author id/name + text + published time + the owner/moderator/member standing;
/// the paging cursor and API-directed poll delay ride through; and auth/expiry (403) and a dead chat (404) map to
/// the closed <c>MISSING_SCOPE</c> / <c>NOT_FOUND</c> failure codes. Also proves the broadcaster's bearer is sent.
/// </summary>
public sealed class YouTubeLiveChatClientTests
{
    private const string Token = "ya29.broadcaster-token";

    private static YouTubeLiveChatClient Build(StubHttpMessageHandler handler) =>
        new(new SingleClientFactory(handler), NullLogger<YouTubeLiveChatClient>.Instance);

    [Fact]
    public async Task GetActiveLiveChats_maps_the_active_broadcast_and_sends_the_bearer()
    {
        StubHttpMessageHandler handler = new(
            (
                HttpStatusCode.OK,
                """{"items":[{"id":"bcast1","snippet":{"liveChatId":"chat123","title":"My Stream"}}]}"""
            )
        );
        YouTubeLiveChatClient sut = Build(handler);

        Result<IReadOnlyList<YouTubeActiveChat>> result = await sut.GetActiveLiveChatsAsync(Token);

        result.IsSuccess.Should().BeTrue();
        YouTubeActiveChat chat = result.Value.Should().ContainSingle().Subject;
        chat.BroadcastId.Should().Be("bcast1");
        chat.LiveChatId.Should().Be("chat123");
        chat.Title.Should().Be("My Stream");
        handler.LastRequest!.RequestUri!.ToString().Should().Contain("broadcastStatus=active");
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be(Token);
    }

    [Fact]
    public async Task GetActiveLiveChats_maps_every_concurrent_active_broadcast_and_its_viewer_count()
    {
        // A channel CAN run more than one concurrent live broadcast — every active item must be tracked,
        // never just the first, and each carries its own liveStreamingDetails.concurrentViewers sample.
        StubHttpMessageHandler handler = new(
            (
                HttpStatusCode.OK,
                """
                {
                  "items": [
                    {
                      "id": "bcast1",
                      "snippet": { "liveChatId": "chat1", "title": "Main encoder" },
                      "liveStreamingDetails": { "concurrentViewers": "1234" }
                    },
                    {
                      "id": "bcast2",
                      "snippet": { "liveChatId": "chat2", "title": "Backup encoder" },
                      "liveStreamingDetails": { "concurrentViewers": "17" }
                    }
                  ]
                }
                """
            )
        );
        YouTubeLiveChatClient sut = Build(handler);

        Result<IReadOnlyList<YouTubeActiveChat>> result = await sut.GetActiveLiveChatsAsync(Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result
            .Value.Should()
            .ContainSingle(c => c.BroadcastId == "bcast1" && c.ConcurrentViewers == 1234L);
        result
            .Value.Should()
            .ContainSingle(c => c.BroadcastId == "bcast2" && c.ConcurrentViewers == 17L);
    }

    [Fact]
    public async Task GetActiveLiveChats_returns_an_empty_list_when_not_live()
    {
        // No active broadcast (or one with chat disabled) is a normal state — success with an empty list.
        StubHttpMessageHandler handler = new((HttpStatusCode.OK, """{"items":[]}"""));
        YouTubeLiveChatClient sut = Build(handler);

        Result<IReadOnlyList<YouTubeActiveChat>> result = await sut.GetActiveLiveChatsAsync(Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveLiveChats_maps_a_403_with_no_reason_to_missing_scope()
    {
        StubHttpMessageHandler handler = new(
            (HttpStatusCode.Forbidden, """{"error":{"code":403}}""")
        );
        YouTubeLiveChatClient sut = Build(handler);

        Result<IReadOnlyList<YouTubeActiveChat>> result = await sut.GetActiveLiveChatsAsync(Token);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("MISSING_SCOPE");
    }

    [Fact]
    public async Task GetActiveLiveChats_maps_a_403_with_insufficientPermissions_to_missing_scope()
    {
        // Regression-proofing: a genuine permission-shaped 403 must still trigger the scope re-auth path.
        StubHttpMessageHandler handler = new(
            (
                HttpStatusCode.Forbidden,
                """{"error":{"code":403,"errors":[{"reason":"insufficientPermissions"}]}}"""
            )
        );
        YouTubeLiveChatClient sut = Build(handler);

        Result<IReadOnlyList<YouTubeActiveChat>> result = await sut.GetActiveLiveChatsAsync(Token);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("MISSING_SCOPE");
    }

    [Fact]
    public async Task GetActiveLiveChats_maps_a_403_with_quotaExceeded_to_quota_exceeded_not_missing_scope()
    {
        // A real quota exhaustion must NOT ride the 15-minute scope-backoff path.
        StubHttpMessageHandler handler = new(
            (
                HttpStatusCode.Forbidden,
                """{"error":{"code":403,"errors":[{"reason":"quotaExceeded"}]}}"""
            )
        );
        YouTubeLiveChatClient sut = Build(handler);

        Result<IReadOnlyList<YouTubeActiveChat>> result = await sut.GetActiveLiveChatsAsync(Token);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("QUOTA_EXCEEDED");
    }

    [Fact]
    public async Task GetActiveLiveChats_maps_a_401_to_missing_scope_regardless_of_body()
    {
        // A 401 is an invalid/expired bearer, never a quota concern — always MISSING_SCOPE.
        StubHttpMessageHandler handler = new(
            (
                HttpStatusCode.Unauthorized,
                """{"error":{"code":401,"errors":[{"reason":"quotaExceeded"}]}}"""
            )
        );
        YouTubeLiveChatClient sut = Build(handler);

        Result<IReadOnlyList<YouTubeActiveChat>> result = await sut.GetActiveLiveChatsAsync(Token);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("MISSING_SCOPE");
    }

    [Fact]
    public async Task ListMessages_maps_messages_cursor_and_poll_interval()
    {
        StubHttpMessageHandler handler = new(
            (
                HttpStatusCode.OK,
                """
                {
                  "pollingIntervalMillis": 3000,
                  "nextPageToken": "TOKEN2",
                  "items": [
                    {
                      "id": "msg1",
                      "snippet": { "displayMessage": "hello world", "publishedAt": "2026-07-10T12:00:00Z" },
                      "authorDetails": {
                        "channelId": "UCauthor",
                        "displayName": "Viewer One",
                        "isChatModerator": true,
                        "isChatOwner": false,
                        "isChatSponsor": true
                      }
                    }
                  ]
                }
                """
            )
        );
        YouTubeLiveChatClient sut = Build(handler);

        Result<YouTubeLiveChatPage> result = await sut.ListMessagesAsync(Token, "chat123", null);

        result.IsSuccess.Should().BeTrue();
        result.Value.PollingIntervalMs.Should().Be(3000);
        result.Value.NextPageToken.Should().Be("TOKEN2");
        result.Value.Messages.Should().ContainSingle();
        YouTubeLiveChatMessage message = result.Value.Messages[0];
        message.Id.Should().Be("msg1");
        message.AuthorChannelId.Should().Be("UCauthor");
        message.AuthorDisplayName.Should().Be("Viewer One");
        message.DisplayText.Should().Be("hello world");
        message.PublishedAt.Should().Be(new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero));
        message.IsModerator.Should().BeTrue();
        message.IsOwner.Should().BeFalse();
        message.IsMember.Should().BeTrue();
        handler.LastRequest!.RequestUri!.ToString().Should().Contain("liveChatId=chat123");
    }

    [Fact]
    public async Task ListMessages_forwards_the_page_token()
    {
        StubHttpMessageHandler handler = new(
            (HttpStatusCode.OK, """{"pollingIntervalMillis":2000,"items":[]}""")
        );
        YouTubeLiveChatClient sut = Build(handler);

        await sut.ListMessagesAsync(Token, "chat123", "PREV_TOKEN");

        handler.LastRequest!.RequestUri!.ToString().Should().Contain("pageToken=PREV_TOKEN");
    }

    [Fact]
    public async Task ListMessages_maps_a_404_to_not_found()
    {
        // The chat ended / the id is stale — the poller must re-resolve the active broadcast.
        StubHttpMessageHandler handler = new(
            (HttpStatusCode.NotFound, """{"error":{"code":404}}""")
        );
        YouTubeLiveChatClient sut = Build(handler);

        Result<YouTubeLiveChatPage> result = await sut.ListMessagesAsync(Token, "chat123", null);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    // ── S030-a — snippet.type supporter events map into the message's typed detail records ──────

    [Fact]
    public async Task ListMessages_maps_a_super_chat_event_to_its_detail_record()
    {
        StubHttpMessageHandler handler = new(
            (
                HttpStatusCode.OK,
                """
                {
                  "pollingIntervalMillis": 3000,
                  "items": [
                    {
                      "id": "sc-1",
                      "snippet": {
                        "type": "superChatEvent",
                        "publishedAt": "2026-07-10T12:00:00Z",
                        "superChatDetails": {
                          "amountMicros": 5000000,
                          "currency": "USD",
                          "amountDisplayString": "$5.00",
                          "userComment": "great stream!",
                          "tier": 2
                        }
                      },
                      "authorDetails": {
                        "channelId": "UCsuperchatter",
                        "displayName": "Super Chatter",
                        "isChatModerator": false,
                        "isChatOwner": false,
                        "isChatSponsor": false
                      }
                    }
                  ]
                }
                """
            )
        );
        YouTubeLiveChatClient sut = Build(handler);

        Result<YouTubeLiveChatPage> result = await sut.ListMessagesAsync(Token, "chat123", null);

        YouTubeLiveChatMessage message = result.Value.Messages.Should().ContainSingle().Subject;
        message.SnippetType.Should().Be("superChatEvent");
        message.SuperChatDetails.Should().NotBeNull();
        message.SuperChatDetails!.AmountMicros.Should().Be(5_000_000UL);
        message.SuperChatDetails.Currency.Should().Be("USD");
        message.SuperChatDetails.AmountDisplayString.Should().Be("$5.00");
        message.SuperChatDetails.UserComment.Should().Be("great stream!");
        message.SuperChatDetails.Tier.Should().Be(2u);
    }

    [Fact]
    public async Task ListMessages_maps_a_super_sticker_event_to_its_detail_record()
    {
        StubHttpMessageHandler handler = new(
            (
                HttpStatusCode.OK,
                """
                {
                  "pollingIntervalMillis": 3000,
                  "items": [
                    {
                      "id": "ss-1",
                      "snippet": {
                        "type": "superStickerEvent",
                        "publishedAt": "2026-07-10T12:00:00Z",
                        "superStickerDetails": {
                          "superStickerMetadata": { "stickerId": "sticker-42", "altText": ":heart:", "language": "en" },
                          "amountMicros": 2000000,
                          "currency": "USD",
                          "amountDisplayString": "$2.00",
                          "tier": 1
                        }
                      },
                      "authorDetails": {
                        "channelId": "UCsticker",
                        "displayName": "Sticker Fan",
                        "isChatModerator": false,
                        "isChatOwner": false,
                        "isChatSponsor": false
                      }
                    }
                  ]
                }
                """
            )
        );
        YouTubeLiveChatClient sut = Build(handler);

        Result<YouTubeLiveChatPage> result = await sut.ListMessagesAsync(Token, "chat123", null);

        YouTubeLiveChatMessage message = result.Value.Messages.Should().ContainSingle().Subject;
        message.SnippetType.Should().Be("superStickerEvent");
        message.SuperStickerDetails.Should().NotBeNull();
        message.SuperStickerDetails!.StickerId.Should().Be("sticker-42");
        message.SuperStickerDetails.AltText.Should().Be(":heart:");
        message.SuperStickerDetails.AmountMicros.Should().Be(2_000_000UL);
        message.SuperStickerDetails.Currency.Should().Be("USD");
        message.SuperStickerDetails.AmountDisplayString.Should().Be("$2.00");
        message.SuperStickerDetails.Tier.Should().Be(1u);
    }

    [Fact]
    public async Task ListMessages_maps_a_new_sponsor_event_to_its_detail_record()
    {
        StubHttpMessageHandler handler = new(
            (
                HttpStatusCode.OK,
                """
                {
                  "pollingIntervalMillis": 3000,
                  "items": [
                    {
                      "id": "ns-1",
                      "snippet": {
                        "type": "newSponsorEvent",
                        "publishedAt": "2026-07-10T12:00:00Z",
                        "newSponsorDetails": { "memberLevelName": "Bronze Member", "isUpgrade": false }
                      },
                      "authorDetails": {
                        "channelId": "UCnewmember",
                        "displayName": "New Member",
                        "isChatModerator": false,
                        "isChatOwner": false,
                        "isChatSponsor": true
                      }
                    }
                  ]
                }
                """
            )
        );
        YouTubeLiveChatClient sut = Build(handler);

        Result<YouTubeLiveChatPage> result = await sut.ListMessagesAsync(Token, "chat123", null);

        YouTubeLiveChatMessage message = result.Value.Messages.Should().ContainSingle().Subject;
        message.SnippetType.Should().Be("newSponsorEvent");
        message.NewSponsorDetails.Should().NotBeNull();
        message.NewSponsorDetails!.MemberLevelName.Should().Be("Bronze Member");
        message.NewSponsorDetails.IsUpgrade.Should().BeFalse();
    }

    [Fact]
    public async Task ListMessages_maps_a_member_milestone_chat_event_to_its_detail_record()
    {
        StubHttpMessageHandler handler = new(
            (
                HttpStatusCode.OK,
                """
                {
                  "pollingIntervalMillis": 3000,
                  "items": [
                    {
                      "id": "mm-1",
                      "snippet": {
                        "type": "memberMilestoneChatEvent",
                        "publishedAt": "2026-07-10T12:00:00Z",
                        "memberMilestoneChatDetails": {
                          "userComment": "6 months strong!",
                          "memberMonth": 6,
                          "memberLevelName": "Silver Member"
                        }
                      },
                      "authorDetails": {
                        "channelId": "UCmilestone",
                        "displayName": "Loyal Member",
                        "isChatModerator": false,
                        "isChatOwner": false,
                        "isChatSponsor": true
                      }
                    }
                  ]
                }
                """
            )
        );
        YouTubeLiveChatClient sut = Build(handler);

        Result<YouTubeLiveChatPage> result = await sut.ListMessagesAsync(Token, "chat123", null);

        YouTubeLiveChatMessage message = result.Value.Messages.Should().ContainSingle().Subject;
        message.SnippetType.Should().Be("memberMilestoneChatEvent");
        message.MemberMilestoneChatDetails.Should().NotBeNull();
        message.MemberMilestoneChatDetails!.UserComment.Should().Be("6 months strong!");
        message.MemberMilestoneChatDetails.MemberMonth.Should().Be(6u);
        message.MemberMilestoneChatDetails.MemberLevelName.Should().Be("Silver Member");
    }

    [Fact]
    public async Task ListMessages_maps_a_membership_gifting_event_to_its_detail_record()
    {
        StubHttpMessageHandler handler = new(
            (
                HttpStatusCode.OK,
                """
                {
                  "pollingIntervalMillis": 3000,
                  "items": [
                    {
                      "id": "mg-1",
                      "snippet": {
                        "type": "membershipGiftingEvent",
                        "publishedAt": "2026-07-10T12:00:00Z",
                        "membershipGiftingDetails": { "giftMembershipsCount": 5, "giftMembershipsLevelName": "Gold Member" }
                      },
                      "authorDetails": {
                        "channelId": "UCgifter",
                        "displayName": "Generous Gifter",
                        "isChatModerator": false,
                        "isChatOwner": false,
                        "isChatSponsor": false
                      }
                    }
                  ]
                }
                """
            )
        );
        YouTubeLiveChatClient sut = Build(handler);

        Result<YouTubeLiveChatPage> result = await sut.ListMessagesAsync(Token, "chat123", null);

        YouTubeLiveChatMessage message = result.Value.Messages.Should().ContainSingle().Subject;
        message.SnippetType.Should().Be("membershipGiftingEvent");
        message.MembershipGiftingDetails.Should().NotBeNull();
        message.MembershipGiftingDetails!.GiftMembershipsCount.Should().Be(5);
        message.MembershipGiftingDetails.GiftMembershipsLevelName.Should().Be("Gold Member");
    }

    [Fact]
    public async Task ListMessages_maps_a_gift_membership_received_event_to_its_detail_record()
    {
        StubHttpMessageHandler handler = new(
            (
                HttpStatusCode.OK,
                """
                {
                  "pollingIntervalMillis": 3000,
                  "items": [
                    {
                      "id": "gr-1",
                      "snippet": {
                        "type": "giftMembershipReceivedEvent",
                        "publishedAt": "2026-07-10T12:00:00Z",
                        "giftMembershipReceivedDetails": {
                          "memberLevelName": "Gold Member",
                          "gifterChannelId": "UCgifter",
                          "associatedMembershipGiftingMessageId": "mg-1"
                        }
                      },
                      "authorDetails": {
                        "channelId": "UCrecipient",
                        "displayName": "Lucky Recipient",
                        "isChatModerator": false,
                        "isChatOwner": false,
                        "isChatSponsor": true
                      }
                    }
                  ]
                }
                """
            )
        );
        YouTubeLiveChatClient sut = Build(handler);

        Result<YouTubeLiveChatPage> result = await sut.ListMessagesAsync(Token, "chat123", null);

        YouTubeLiveChatMessage message = result.Value.Messages.Should().ContainSingle().Subject;
        message.SnippetType.Should().Be("giftMembershipReceivedEvent");
        message.GiftMembershipReceivedDetails.Should().NotBeNull();
        message.GiftMembershipReceivedDetails!.MemberLevelName.Should().Be("Gold Member");
        message.GiftMembershipReceivedDetails.GifterChannelId.Should().Be("UCgifter");
        message
            .GiftMembershipReceivedDetails.AssociatedMembershipGiftingMessageId.Should()
            .Be("mg-1");
    }

    [Fact]
    public async Task GetOwnChannel_maps_the_channel_id_and_title_and_sends_the_bearer()
    {
        StubHttpMessageHandler handler = new(
            (
                HttpStatusCode.OK,
                """{"items":[{"id":"UCstreamer","snippet":{"title":"Streamer YT"}}]}"""
            )
        );
        YouTubeLiveChatClient sut = Build(handler);

        Result<YouTubeOwnChannel> result = await sut.GetOwnChannelAsync(Token);

        result.IsSuccess.Should().BeTrue();
        result.Value.ChannelId.Should().Be("UCstreamer");
        result.Value.Title.Should().Be("Streamer YT");
        handler.LastRequest!.RequestUri!.ToString().Should().Contain("mine=true");
        handler.LastRequest.Headers.Authorization!.Parameter.Should().Be(Token);
    }

    [Fact]
    public async Task GetOwnChannel_maps_a_google_account_without_a_channel_to_not_found()
    {
        StubHttpMessageHandler handler = new((HttpStatusCode.OK, """{"items":[]}"""));
        YouTubeLiveChatClient sut = Build(handler);

        Result<YouTubeOwnChannel> result = await sut.GetOwnChannelAsync(Token);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task SendMessage_posts_the_text_into_the_chat_with_the_bearer()
    {
        StubHttpMessageHandler handler = new((HttpStatusCode.OK, """{"id":"sent-1"}"""));
        YouTubeLiveChatClient sut = Build(handler);

        Result result = await sut.SendMessageAsync(Token, "chat123", "hello viewers");

        result.IsSuccess.Should().BeTrue();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.ToString().Should().Contain("liveChatMessages");
        handler.LastRequest.Headers.Authorization!.Parameter.Should().Be(Token);
        string body = await handler.LastRequest.Content!.ReadAsStringAsync();
        body.Should()
            .Contain("chat123")
            .And.Contain("hello viewers")
            .And.Contain("textMessageEvent");
    }

    [Fact]
    public async Task SendMessage_rejects_an_over_200_char_message_before_any_call()
    {
        StubHttpMessageHandler handler = new((HttpStatusCode.OK, "{}"));
        YouTubeLiveChatClient sut = Build(handler);

        Result result = await sut.SendMessageAsync(Token, "chat123", new('a', 201));

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        handler.LastRequest.Should().BeNull("a guaranteed 400 must not burn a quota-billed call");
    }

    [Fact]
    public async Task SendMessage_maps_a_403_to_missing_scope()
    {
        StubHttpMessageHandler handler = new(
            (HttpStatusCode.Forbidden, """{"error":{"code":403}}""")
        );
        YouTubeLiveChatClient sut = Build(handler);

        Result result = await sut.SendMessageAsync(Token, "chat123", "hi");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("MISSING_SCOPE");
    }

    [Fact]
    public async Task SendMessage_maps_a_403_with_rateLimitExceeded_to_quota_exceeded()
    {
        // A reply write hitting Google's rate limit must surface as quota burn, not a scope problem —
        // done-when: quota burn shows as quota.
        StubHttpMessageHandler handler = new(
            (
                HttpStatusCode.Forbidden,
                """{"error":{"code":403,"errors":[{"reason":"rateLimitExceeded"}]}}"""
            )
        );
        YouTubeLiveChatClient sut = Build(handler);

        Result result = await sut.SendMessageAsync(Token, "chat123", "hi");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("QUOTA_EXCEEDED");
    }

    [Fact]
    public async Task BanUser_maps_a_403_with_dailyLimitExceeded_to_quota_exceeded()
    {
        // A ban write hitting Google's daily quota must surface as quota burn, not MISSING_SCOPE.
        StubHttpMessageHandler handler = new(
            (
                HttpStatusCode.Forbidden,
                """{"error":{"code":403,"errors":[{"reason":"dailyLimitExceeded"}]}}"""
            )
        );
        YouTubeLiveChatClient sut = Build(handler);

        Result<string> result = await sut.BanUserAsync(Token, "chat123", "UCbad", null);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("QUOTA_EXCEEDED");
    }

    [Fact]
    public async Task BanUser_maps_a_403_with_forbidden_reason_to_missing_scope()
    {
        // A generic "forbidden" reason (no quota keyword) still defaults to the scope re-auth path.
        StubHttpMessageHandler handler = new(
            (
                HttpStatusCode.Forbidden,
                """{"error":{"code":403,"errors":[{"reason":"forbidden"}]}}"""
            )
        );
        YouTubeLiveChatClient sut = Build(handler);

        Result<string> result = await sut.BanUserAsync(Token, "chat123", "UCbad", null);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("MISSING_SCOPE");
    }

    [Fact]
    public async Task BanUser_with_a_duration_posts_a_temporary_ban_and_returns_the_ban_id()
    {
        StubHttpMessageHandler handler = new((HttpStatusCode.OK, """{"id":"ban-1"}"""));
        YouTubeLiveChatClient sut = Build(handler);

        Result<string> result = await sut.BanUserAsync(Token, "chat123", "UCbad", 600);

        result.IsSuccess.Should().BeTrue();
        // The returned resource id is the ONLY key liveChatBans.delete accepts — it must survive the parse.
        result.Value.Should().Be("ban-1");
        handler.LastRequest!.RequestUri!.ToString().Should().Contain("liveChat/bans");
        string body = await handler.LastRequest.Content!.ReadAsStringAsync();
        body.Should()
            .Contain("temporary")
            .And.Contain("600")
            .And.Contain("UCbad")
            .And.Contain("chat123");
    }

    [Fact]
    public async Task BanUser_without_a_duration_posts_a_permanent_ban()
    {
        StubHttpMessageHandler handler = new((HttpStatusCode.OK, """{"id":"ban-2"}"""));
        YouTubeLiveChatClient sut = Build(handler);

        Result<string> result = await sut.BanUserAsync(Token, "chat123", "UCworse", null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ban-2");
        string body = await handler.LastRequest!.Content!.ReadAsStringAsync();
        body.Should().Contain("permanent").And.NotContain("banDurationSeconds");
    }

    [Fact]
    public async Task BanUser_with_an_id_less_response_fails_instead_of_returning_an_unusable_ban()
    {
        // A 2xx without a resource id would ledger an empty key and make the later unban a silent lie.
        StubHttpMessageHandler handler = new((HttpStatusCode.OK, "{}"));
        YouTubeLiveChatClient sut = Build(handler);

        Result<string> result = await sut.BanUserAsync(Token, "chat123", "UCbad", null);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("SERVICE_UNAVAILABLE");
    }

    [Fact]
    public async Task UnbanUser_deletes_the_ban_resource_by_its_id()
    {
        StubHttpMessageHandler handler = new((HttpStatusCode.NoContent, "{}"));
        YouTubeLiveChatClient sut = Build(handler);

        Result result = await sut.UnbanUserAsync(Token, "ban-1");

        result.IsSuccess.Should().BeTrue();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        handler.LastRequest.RequestUri!.ToString().Should().Contain("liveChat/bans?id=ban-1");
        handler.LastRequest.Headers.Authorization!.Parameter.Should().Be(Token);
    }

    [Fact]
    public async Task UnbanUser_maps_a_gone_ban_to_not_found()
    {
        // An expired timeout or ended chat: YouTube no longer has the ban — the platform treats NOT_FOUND
        // as "already unbanned", so the mapping must hold.
        StubHttpMessageHandler handler = new((HttpStatusCode.NotFound, "{}"));
        YouTubeLiveChatClient sut = Build(handler);

        Result result = await sut.UnbanUserAsync(Token, "ban-gone");

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task UpdateActiveBroadcastTitle_puts_the_new_title_with_the_carried_over_start_time()
    {
        // The PUT replaces the snippet, so the client must fetch the active broadcast first and carry its
        // scheduledStartTime over — dropping it would 400 (required on a snippet update).
        StubHttpMessageHandler handler = new(
            (
                HttpStatusCode.OK,
                """{"items":[{"id":"bcast-1","snippet":{"liveChatId":"chat123","title":"old","scheduledStartTime":"2026-07-11T18:00:00Z"}}]}"""
            ),
            (HttpStatusCode.OK, """{"id":"bcast-1"}""")
        );
        YouTubeLiveChatClient sut = Build(handler);

        Result<string> result = await sut.UpdateActiveBroadcastTitleAsync(Token, "new title");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("new title");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Put);
        handler.LastRequest.RequestUri!.ToString().Should().Contain("liveBroadcasts?part=snippet");
        string body = await handler.LastRequest.Content!.ReadAsStringAsync();
        body.Should()
            .Contain("bcast-1")
            .And.Contain("new title")
            .And.Contain("2026-07-11T18:00:00Z");
    }

    [Fact]
    public async Task UpdateActiveBroadcastTitle_offline_is_not_found_without_a_put()
    {
        StubHttpMessageHandler handler = new((HttpStatusCode.OK, """{"items":[]}"""));
        YouTubeLiveChatClient sut = Build(handler);

        Result<string> result = await sut.UpdateActiveBroadcastTitleAsync(Token, "t");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get, "no PUT may follow a miss");
    }

    [Fact]
    public async Task UpdateActiveBroadcastTitle_rejects_an_over_100_char_title_before_any_call()
    {
        StubHttpMessageHandler handler = new((HttpStatusCode.OK, "{}"));
        YouTubeLiveChatClient sut = Build(handler);

        Result<string> result = await sut.UpdateActiveBroadcastTitleAsync(Token, new('t', 101));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        handler.LastRequest.Should().BeNull("a guaranteed 400 must not burn a quota-billed call");
    }

    [Fact]
    public async Task UpdateActiveBroadcastTitle_retitles_every_active_broadcast_not_just_the_first()
    {
        // A channel can run more than one concurrent broadcast (simultaneous multi-encoder streams) —
        // the retitle must reach every one of them, proven by two distinct PUT calls carrying each
        // broadcast's own id.
        StubHttpMessageHandler handler = new(
            (
                HttpStatusCode.OK,
                """
                {"items":[
                  {"id":"bcast-1","snippet":{"liveChatId":"chat1","title":"old1","scheduledStartTime":"2026-07-11T18:00:00Z"}},
                  {"id":"bcast-2","snippet":{"liveChatId":"chat2","title":"old2","scheduledStartTime":"2026-07-11T19:00:00Z"}}
                ]}
                """
            ),
            (HttpStatusCode.OK, """{"id":"bcast-1"}"""),
            (HttpStatusCode.OK, """{"id":"bcast-2"}""")
        );
        YouTubeLiveChatClient sut = Build(handler);

        Result<string> result = await sut.UpdateActiveBroadcastTitleAsync(Token, "new title");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("new title");
        // 1 GET + 2 PUTs — one per active broadcast.
        handler.Requests.Should().HaveCount(3);
        List<HttpRequestMessage> puts = handler
            .Requests.Where(r => r.Method == HttpMethod.Put)
            .ToList();
        puts.Should().HaveCount(2);
        string firstBody = await puts[0].Content!.ReadAsStringAsync();
        string secondBody = await puts[1].Content!.ReadAsStringAsync();
        firstBody.Should().Contain("bcast-1").And.Contain("new title");
        secondBody.Should().Contain("bcast-2").And.Contain("new title");
    }

    [Fact]
    public async Task UpdateActiveBroadcastTitle_surfaces_a_partial_failure_instead_of_reporting_success()
    {
        // One of two active broadcasts fails its PUT — the caller must learn that, not be told every
        // broadcast was retitled when only one actually was.
        StubHttpMessageHandler handler = new(
            (
                HttpStatusCode.OK,
                """
                {"items":[
                  {"id":"bcast-1","snippet":{"liveChatId":"chat1","title":"old1","scheduledStartTime":"2026-07-11T18:00:00Z"}},
                  {"id":"bcast-2","snippet":{"liveChatId":"chat2","title":"old2","scheduledStartTime":"2026-07-11T19:00:00Z"}}
                ]}
                """
            ),
            (HttpStatusCode.OK, """{"id":"bcast-1"}"""),
            (HttpStatusCode.InternalServerError, "{}")
        );
        YouTubeLiveChatClient sut = Build(handler);

        Result<string> result = await sut.UpdateActiveBroadcastTitleAsync(Token, "new title");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("PARTIAL_FAILURE");
        List<HttpRequestMessage> puts = handler
            .Requests.Where(r => r.Method == HttpMethod.Put)
            .ToList();
        puts.Should()
            .HaveCount(2, "both broadcasts must still be attempted despite the first failing");
    }

    [Fact]
    public async Task DeleteMessage_deletes_by_the_message_id_with_the_bearer()
    {
        StubHttpMessageHandler handler = new((HttpStatusCode.NoContent, "{}"));
        YouTubeLiveChatClient sut = Build(handler);

        Result result = await sut.DeleteMessageAsync(Token, "msg-9");

        result.IsSuccess.Should().BeTrue();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Delete);
        handler.LastRequest.RequestUri!.ToString().Should().Contain("liveChat/messages?id=msg-9");
        handler.LastRequest.Headers.Authorization!.Parameter.Should().Be(Token);
    }

    private sealed class StubHttpMessageHandler(
        params (HttpStatusCode Status, string Json)[] responses
    ) : HttpMessageHandler
    {
        private int _index;

        public HttpRequestMessage? LastRequest { get; private set; }

        // Every request sent through this handler, in order — needed to prove a multi-broadcast title
        // update PUTs each broadcast rather than just the first.
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            // Buffer the content BEFORE the request is disposed so header/uri assertions survive.
            if (request.Content is not null)
                await request.Content.LoadIntoBufferAsync();
            LastRequest = request;
            Requests.Add(request);

            (HttpStatusCode status, string json) = responses[
                Math.Min(_index, responses.Length - 1)
            ];
            _index++;
            return new(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
