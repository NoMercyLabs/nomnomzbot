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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Infrastructure.Chat;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Seeding;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// The one delivery point of an authored reply: chat, overlay, both or none, with the chat leg threaded under
/// the triggering message and the overlay leg shown under the channel's bot identity.
/// </summary>
public sealed class ReplyDispatcherTests
{
    private static readonly Guid Channel = Guid.Parse("0197d6a0-0000-7000-8000-000000000001");
    private const string BotUserId = "bot-77";
    private const string BotName = "NomBotTest";

    private static readonly ReplyOrigin Origin = new("twitch", "msg-1", "Viewer One", "viewerone");

    [Fact]
    public async Task Chat_target_threads_the_reply_under_the_triggering_message_and_touches_no_overlay()
    {
        Rig rig = new();

        ReplyOutcome outcome = await rig.Sut.DispatchAsync(
            Request(ReplyDelivery.ChatOnly, "hello", origin: Origin)
        );

        rig.Chat.Sent.Should().Equal(("twitch", "reply:msg-1", "hello"));
        rig.Overlay.Lines.Should().BeEmpty();
        outcome.Should().Be(new ReplyOutcome(true, false, false));
    }

    [Fact]
    public async Task A_rejected_reply_form_falls_back_to_a_plain_message_that_mentions_the_viewer()
    {
        Rig rig = new() { Chat = { RejectReplies = true } };

        ReplyOutcome outcome = await rig.Sut.DispatchAsync(
            Request(ReplyDelivery.ChatOnly, "hello", origin: Origin)
        );

        rig.Chat.Sent.Should().Equal(("twitch", "plain", "@viewerone hello"));
        outcome.SentToChat.Should().BeTrue();
    }

    [Fact]
    public async Task An_origin_without_a_message_id_sends_a_plain_message_on_that_platform()
    {
        Rig rig = new();

        await rig.Sut.DispatchAsync(
            Request(
                ReplyDelivery.ChatOnly,
                "hello",
                origin: new ReplyOrigin("kick", "", "Viewer One", "viewerone")
            )
        );

        rig.Chat.Sent.Should().Equal(("kick", "plain", "hello"));
    }

    [Fact]
    public async Task Without_an_origin_the_chat_leg_uses_the_channels_own_chat_provider()
    {
        Rig rig = new();

        ReplyOutcome outcome = await rig.Sut.DispatchAsync(
            Request(ReplyDelivery.ChatOnly, "hello", origin: null)
        );

        rig.Provider.Sent.Should().Equal((Channel, "hello"));
        rig.Chat.Sent.Should().BeEmpty();
        outcome.SentToChat.Should().BeTrue();
    }

    [Fact]
    public async Task A_failed_chat_send_is_reported_as_not_sent()
    {
        Rig rig = new() { Chat = { RejectEverything = true } };

        ReplyOutcome outcome = await rig.Sut.DispatchAsync(
            Request(ReplyDelivery.ChatOnly, "hello", origin: Origin)
        );

        outcome.SentToChat.Should().BeFalse();
        outcome.Succeeded(ReplyDelivery.ChatOnly).Should().BeFalse();
    }

    [Fact]
    public async Task Overlay_with_html_sends_nothing_to_chat_and_one_sanitized_line_under_the_bot_identity()
    {
        Rig rig = new();

        ReplyOutcome outcome = await rig.Sut.DispatchAsync(
            Request(
                new ReplyDelivery(ReplyTarget.Overlay, Html: true),
                "",
                overlayText: "<marquee>CHOO CHOO Stoney</marquee><script>steal()</script>",
                origin: Origin
            )
        );

        rig.Chat.Sent.Should().BeEmpty();
        rig.Provider.Sent.Should().BeEmpty();
        rig.Overlay.Lines.Should()
            .Equal(
                new ChatOverlayLine(
                    Channel,
                    BotUserId,
                    BotName,
                    "<marquee>CHOO CHOO Stoney</marquee>",
                    IsHtml: true
                )
            );
        outcome.Should().Be(new ReplyOutcome(false, true, false));
    }

    [Fact]
    public async Task Overlay_without_html_passes_the_text_through_as_plain_text()
    {
        Rig rig = new();

        await rig.Sut.DispatchAsync(
            Request(
                new ReplyDelivery(ReplyTarget.Overlay, Html: false),
                "",
                overlayText: "<b>not bold</b>",
                origin: Origin
            )
        );

        rig.Overlay.Lines.Should()
            .Equal(
                new ChatOverlayLine(Channel, BotUserId, BotName, "<b>not bold</b>", IsHtml: false)
            );
    }

    [Fact]
    public async Task Without_a_dedicated_bot_the_overlay_line_carries_the_channel_id_and_the_fallback_name()
    {
        Rig rig = new() { Bot = null };

        await rig.Sut.DispatchAsync(
            Request(new ReplyDelivery(ReplyTarget.Overlay), "", overlayText: "hi", origin: Origin)
        );

        rig.Overlay.Lines.Should()
            .Equal(new ChatOverlayLine(Channel, Channel.ToString(), "NomNomzBot", "hi", false));
    }

    [Fact]
    public async Task Overlay_with_no_chat_widget_open_sends_nothing_reports_it_missing_and_logs_a_warning()
    {
        Rig rig = new() { Overlay = { Result = ChatOverlayDelivery.NoChatOverlayOpen } };

        ReplyOutcome outcome = await rig.Sut.DispatchAsync(
            Request(new ReplyDelivery(ReplyTarget.Overlay), "", overlayText: "hi", origin: Origin)
        );

        rig.Chat.Sent.Should().BeEmpty();
        rig.Provider.Sent.Should().BeEmpty();
        outcome.Should().Be(new ReplyOutcome(false, false, true));
        outcome.Succeeded(new ReplyDelivery(ReplyTarget.Overlay)).Should().BeFalse();
        rig.Log.Entries.Should()
            .ContainSingle(e =>
                e.Level == LogLevel.Warning && e.Message.Contains("no chat overlay is open")
            );
    }

    [Fact]
    public async Task A_missing_overlay_asks_the_obs_browser_source_recovery_for_that_channel_once()
    {
        Rig rig = new() { Overlay = { Result = ChatOverlayDelivery.NoChatOverlayOpen } };

        await rig.Sut.DispatchAsync(
            Request(new ReplyDelivery(ReplyTarget.Overlay), "", overlayText: "hi", origin: Origin)
        );

        rig.Recovery.Received(1).Schedule(Channel);
        rig.Recovery.ReceivedCalls().Should().ContainSingle();
    }

    [Fact]
    public async Task A_delivered_overlay_line_and_a_chat_only_reply_never_ask_for_recovery()
    {
        Rig rig = new();

        await rig.Sut.DispatchAsync(
            Request(new ReplyDelivery(ReplyTarget.Overlay), "", overlayText: "hi", origin: Origin)
        );
        await rig.Sut.DispatchAsync(Request(ReplyDelivery.ChatOnly, "hi", origin: Origin));

        rig.Recovery.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task The_html_overlay_line_carries_the_requester_beside_the_text_and_never_inside_it()
    {
        Rig rig = new();

        await rig.Sut.DispatchAsync(
            Request(
                new ReplyDelivery(ReplyTarget.Overlay, Html: true),
                "",
                overlayText: "<marquee>CHOO CHOO</marquee>",
                origin: Origin,
                requestedBy: "Viewer<script>x()</script>"
            )
        );

        // The widget draws "requested by ..." from this field as text. The sanitized HTML stays exactly the
        // channel's own line, so a viewer name can never become markup.
        rig.Overlay.Lines.Should()
            .Equal(
                new ChatOverlayLine(
                    Channel,
                    BotUserId,
                    BotName,
                    "<marquee>CHOO CHOO</marquee>",
                    IsHtml: true,
                    RequestedBy: "Viewer<script>x()</script>"
                )
            );
    }

    [Fact]
    public async Task The_plain_overlay_line_carries_the_requester_and_a_chat_reply_never_names_them()
    {
        Rig rig = new();

        await rig.Sut.DispatchAsync(
            Request(
                new ReplyDelivery(ReplyTarget.Both),
                "choo choo",
                origin: Origin,
                requestedBy: "Viewer One"
            )
        );

        rig.Chat.Sent.Should().Equal(("twitch", "reply:msg-1", "choo choo"));
        rig.Overlay.Lines.Should()
            .Equal(
                new ChatOverlayLine(
                    Channel,
                    BotUserId,
                    BotName,
                    "choo choo",
                    IsHtml: false,
                    RequestedBy: "Viewer One"
                )
            );
    }

    [Fact]
    public async Task A_blank_requester_means_no_attribution()
    {
        Rig rig = new();

        await rig.Sut.DispatchAsync(
            Request(
                new ReplyDelivery(ReplyTarget.Overlay),
                "",
                overlayText: "hi",
                origin: Origin,
                requestedBy: "   "
            )
        );

        rig.Overlay.Lines.Should().ContainSingle().Which.RequestedBy.Should().BeNull();
    }

    [Fact]
    public async Task The_overlay_line_carries_the_look_the_channel_chose_and_the_chat_reply_ignores_it()
    {
        Rig rig = new();
        ReplyLook look = new(ReplyHighlight.Success, ReplySize.Large, DurationSeconds: 20);

        await rig.Sut.DispatchAsync(
            Request(
                new ReplyDelivery(ReplyTarget.Both, Look: look),
                "choo choo",
                origin: Origin,
                requestedBy: "Viewer One"
            )
        );

        rig.Chat.Sent.Should().Equal(("twitch", "reply:msg-1", "choo choo"));
        ChatOverlayLine line = rig.Overlay.Lines.Should().ContainSingle().Subject;
        line.Look.Should().Be(look);
        line.Look!.HighlightToken.Should().Be("success");
        line.Look.SizeToken.Should().Be("large");
    }

    [Fact]
    public async Task A_delivery_without_a_look_sends_a_line_without_a_look()
    {
        Rig rig = new();

        await rig.Sut.DispatchAsync(
            Request(new ReplyDelivery(ReplyTarget.Overlay), "", overlayText: "hi", origin: Origin)
        );

        rig.Overlay.Lines.Should().ContainSingle().Which.Look.Should().BeNull();
    }

    [Fact]
    public async Task Both_sends_the_chat_text_to_chat_and_the_overlay_text_to_the_overlay()
    {
        Rig rig = new();

        ReplyOutcome outcome = await rig.Sut.DispatchAsync(
            Request(
                new ReplyDelivery(ReplyTarget.Both, Html: true),
                "plain hi",
                overlayText: "<i>fancy hi</i>",
                origin: Origin
            )
        );

        rig.Chat.Sent.Should().Equal(("twitch", "reply:msg-1", "plain hi"));
        rig.Overlay.Lines.Should()
            .Equal(new ChatOverlayLine(Channel, BotUserId, BotName, "<i>fancy hi</i>", true));
        outcome.Should().Be(new ReplyOutcome(true, true, false));
    }

    [Fact]
    public async Task Both_without_separate_overlay_text_shows_the_chat_text_on_the_overlay()
    {
        Rig rig = new();

        await rig.Sut.DispatchAsync(
            Request(new ReplyDelivery(ReplyTarget.Both), "same text", origin: Origin)
        );

        rig.Overlay.Lines.Should()
            .Equal(new ChatOverlayLine(Channel, BotUserId, BotName, "same text", false));
    }

    [Fact]
    public async Task Both_with_the_overlay_closed_still_answers_in_chat_and_counts_as_delivered()
    {
        Rig rig = new() { Overlay = { Result = ChatOverlayDelivery.NoChatOverlayOpen } };
        ReplyDelivery both = new(ReplyTarget.Both);

        ReplyOutcome outcome = await rig.Sut.DispatchAsync(Request(both, "hi", origin: Origin));

        rig.Chat.Sent.Should().Equal(("twitch", "reply:msg-1", "hi"));
        outcome.Should().Be(new ReplyOutcome(true, false, true));
        outcome.Succeeded(both).Should().BeTrue();
    }

    [Fact]
    public async Task None_sends_nothing_anywhere_and_counts_as_delivered()
    {
        Rig rig = new();
        ReplyDelivery none = new(ReplyTarget.None);

        ReplyOutcome outcome = await rig.Sut.DispatchAsync(Request(none, "hi", origin: Origin));

        rig.Chat.Sent.Should().BeEmpty();
        rig.Provider.Sent.Should().BeEmpty();
        rig.Overlay.Lines.Should().BeEmpty();
        outcome.Should().Be(new ReplyOutcome(false, false, false));
        outcome.Succeeded(none).Should().BeTrue();
    }

    [Fact]
    public async Task The_chat_send_runs_under_the_sanction_the_caller_holds()
    {
        Rig rig = new();
        OutboundSanction held = OutboundSanction.ChannelConfiguration("command:train");

        using (rig.Sanctions.Begin(held))
        {
            await rig.Sut.DispatchAsync(Request(ReplyDelivery.ChatOnly, "hi", origin: Origin));
            await rig.Sut.DispatchAsync(Request(ReplyDelivery.ChatOnly, "hi", origin: null));
        }

        rig.Chat.SanctionSeen.Should().Be(held);
        rig.Provider.SanctionSeen.Should().Be(held);
    }

    [Theory]
    [InlineData(ReplyTarget.Chat, true, false, true)]
    [InlineData(ReplyTarget.Chat, false, true, false)]
    [InlineData(ReplyTarget.Overlay, false, true, true)]
    [InlineData(ReplyTarget.Overlay, true, false, false)]
    [InlineData(ReplyTarget.Both, true, false, true)]
    [InlineData(ReplyTarget.Both, false, true, true)]
    [InlineData(ReplyTarget.Both, false, false, false)]
    [InlineData(ReplyTarget.None, false, false, true)]
    public void Succeeded_follows_the_target(
        ReplyTarget target,
        bool sentToChat,
        bool sentToOverlay,
        bool expected
    )
    {
        ReplyOutcome outcome = new(sentToChat, sentToOverlay, false);

        outcome.Succeeded(new ReplyDelivery(target)).Should().Be(expected);
    }

    private static ReplyRequest Request(
        ReplyDelivery delivery,
        string text,
        string? overlayText = null,
        ReplyOrigin? origin = null,
        string? requestedBy = null
    ) =>
        new()
        {
            BroadcasterId = Channel,
            Text = text,
            OverlayText = overlayText,
            Delivery = delivery,
            Origin = origin,
            RequestedBy = requestedBy,
        };

    private sealed class Rig
    {
        public RecordingOriginChat Chat { get; }
        public RecordingChatProvider Provider { get; }
        public RecordingChatOverlayLineSender Overlay { get; } = new();
        public ListLogger<ReplyDispatcher> Log { get; } = new();
        public OutboundSanctionAccessor Sanctions { get; } = new();
        public ChannelTwitchBot? Bot { get; set; } = new(BotUserId, BotName);

        public IObsBrowserSourceRecoveryScheduler Recovery { get; } =
            Substitute.For<IObsBrowserSourceRecoveryScheduler>();

        public ReplyDispatcher Sut =>
            new(Chat, Provider.Instance, Overlay, new StubBotResolver(Bot), Recovery, Log);

        public Rig()
        {
            Chat = new RecordingOriginChat(Sanctions);
            Provider = new RecordingChatProvider(Sanctions);
        }
    }

    private sealed class RecordingOriginChat(IOutboundSanctionAccessor sanctions)
        : IInboundOriginChatSender
    {
        public List<(string Provider, string Kind, string Text)> Sent { get; } = [];
        public bool RejectReplies { get; set; }
        public bool RejectEverything { get; set; }
        public OutboundSanction? SanctionSeen { get; private set; }

        public Task<Result> SendMessageAsync(
            Guid broadcasterId,
            string provider,
            string message,
            CancellationToken cancellationToken = default
        )
        {
            SanctionSeen = sanctions.Current;
            if (RejectEverything)
                return Task.FromResult(Result.Failure("rejected", "SEND_FAILED"));
            Sent.Add((provider, "plain", message));
            return Task.FromResult(Result.Success());
        }

        public Task<Result> SendReplyAsync(
            Guid broadcasterId,
            string provider,
            string replyToMessageId,
            string message,
            CancellationToken cancellationToken = default
        )
        {
            SanctionSeen = sanctions.Current;
            if (RejectReplies || RejectEverything)
                return Task.FromResult(Result.Failure("rejected", "SEND_FAILED"));
            Sent.Add((provider, $"reply:{replyToMessageId}", message));
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class RecordingChatProvider
    {
        public RecordingChatProvider(IOutboundSanctionAccessor sanctions)
        {
            Instance = Substitute.For<IChatProvider>();
            Instance
                .SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    SanctionSeen = sanctions.Current;
                    Sent.Add((call.ArgAt<Guid>(0), call.ArgAt<string>(1)));
                    return Task.FromResult(true);
                });
        }

        public IChatProvider Instance { get; }
        public List<(Guid Channel, string Text)> Sent { get; } = [];
        public OutboundSanction? SanctionSeen { get; private set; }
    }

    private sealed class StubBotResolver(ChannelTwitchBot? bot) : IChannelTwitchBotResolver
    {
        public Task<ChannelTwitchBot?> ResolveAsync(
            Guid broadcasterId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(bot);
    }
}
