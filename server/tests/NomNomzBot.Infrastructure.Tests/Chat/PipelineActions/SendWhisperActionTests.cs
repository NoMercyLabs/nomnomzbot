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

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Platform;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Chat.PipelineActions;
using NomNomzBot.Infrastructure.Chat.Whispers;
using NomNomzBot.Infrastructure.Platform.Messaging;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat.PipelineActions;

/// <summary>
/// The <c>send_whisper</c> pipeline action runs the real recipient lookup, the real daily-recipient cap, the
/// real template resolver and the real Twitch <see cref="IPlatformDirectMessageSender"/>; only the two Twitch
/// API clients (the outside world) are faked. Each test holds the action to what reached Twitch: the recipient
/// id and the rendered text, or nothing at all when the action says it failed.
/// </summary>
public sealed class SendWhisperActionTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0198d000-0000-7000-8000-00000000f001");

    private sealed record Rig(
        SendWhisperAction Action,
        ITwitchWhispersApi Whispers,
        ITwitchUsersApi Users,
        WhisperRecipientCap Cap
    );

    private static TwitchUser User(string id, string login, string displayName) =>
        new(
            Id: id,
            Login: login,
            DisplayName: displayName,
            Type: "",
            BroadcasterType: "",
            Description: "",
            ProfileImageUrl: "",
            OfflineImageUrl: "",
            ViewCount: 0,
            CreatedAt: DateTimeOffset.UtcNow
        );

    private static Rig Build(bool twitchSender = true, params TwitchUser[] knownUsers)
    {
        ITwitchWhispersApi whispers = Substitute.For<ITwitchWhispersApi>();
        whispers
            .SendWhisperAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        ITwitchUsersApi users = Substitute.For<ITwitchUsersApi>();
        users
            .GetUsersByLoginsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Result.Success<IReadOnlyList<TwitchUser>>([
                    .. knownUsers.Where(u => call.Arg<IReadOnlyList<string>>().Contains(u.Login)),
                ])
            );
        users
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Result.Success<IReadOnlyList<TwitchUser>>([
                    .. knownUsers.Where(u => call.Arg<IReadOnlyList<string>>().Contains(u.Id)),
                ])
            );

        WhisperRecipientCap cap = new(TimeProvider.System);
        IPlatformDirectMessageSender[] senders = twitchSender
            ? [new TwitchWhisperDirectMessageSender(whispers)]
            : [];
        SendWhisperAction action = new(
            TestTemplateResolver.Create(),
            users,
            TestMentionTargets.LoginsOnly(),
            senders,
            cap
        );
        return new(action, whispers, users, cap);
    }

    private static PipelineExecutionContext Context()
    {
        PipelineExecutionContext ctx = new()
        {
            BroadcasterId = Broadcaster,
            TriggeredByUserId = "999",
            TriggeredByDisplayName = "Viewer1",
            MessageId = "msg-1",
            RawMessage = "!welcome",
        };
        ctx.Variables["user.name"] = "Viewer1";
        return ctx;
    }

    private static ActionDefinition Whisper(string recipient, string message) =>
        new()
        {
            Type = "send_whisper",
            Parameters = new()
            {
                ["recipient"] = JsonSerializer.SerializeToElement(recipient),
                ["message"] = JsonSerializer.SerializeToElement(message),
            },
        };

    [Fact]
    public async Task A_templated_recipient_and_message_reach_twitch_as_the_resolved_user_id_and_text()
    {
        Rig rig = Build(true, User("999", "viewer1", "Viewer1"));

        ActionResult result = await rig.Action.ExecuteAsync(
            Context(),
            Whisper("{user.name}", "Welcome {user.name}, this is a private hello.")
        );

        result.Succeeded.Should().BeTrue();
        result.Output.Should().Be("Welcome Viewer1, this is a private hello.");
        await rig
            .Whispers.Received(1)
            .SendWhisperAsync(
                Broadcaster,
                "999",
                "Welcome Viewer1, this is a private hello.",
                Arg.Any<CancellationToken>()
            );
        rig.Whispers.ReceivedCalls().Should().ContainSingle();
    }

    [Fact]
    public async Task An_at_mention_is_looked_up_as_a_login_and_the_whisper_goes_to_that_id()
    {
        Rig rig = Build(true, User("4242", "someoneelse", "SomeoneElse"));

        ActionResult result = await rig.Action.ExecuteAsync(
            Context(),
            Whisper("@SomeoneElse", "psst")
        );

        result.Succeeded.Should().BeTrue();
        await rig
            .Whispers.Received(1)
            .SendWhisperAsync(Broadcaster, "4242", "psst", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_numeric_recipient_is_a_twitch_user_id_and_is_checked_against_twitch()
    {
        Rig rig = Build(true, User("4242", "someoneelse", "SomeoneElse"));

        ActionResult result = await rig.Action.ExecuteAsync(Context(), Whisper("4242", "psst"));

        result.Succeeded.Should().BeTrue();
        await rig
            .Whispers.Received(1)
            .SendWhisperAsync(Broadcaster, "4242", "psst", Arg.Any<CancellationToken>());
        await rig
            .Users.Received(1)
            .GetUsersByIdsAsync(
                Arg.Is<IReadOnlyList<string>>(ids => ids.Single() == "4242"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_unknown_recipient_fails_with_a_plain_reason_and_sends_nothing()
    {
        Rig rig = Build(true, User("999", "viewer1", "Viewer1"));

        ActionResult result = await rig.Action.ExecuteAsync(
            Context(),
            Whisper("ghostuser", "hello")
        );

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ghostuser").And.Contain("not found");
        rig.Whispers.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task A_recipient_that_is_empty_after_templates_fails_and_sends_nothing()
    {
        Rig rig = Build(true, User("999", "viewer1", "Viewer1"));
        PipelineExecutionContext ctx = Context();
        ctx.Variables["target"] = "  ";

        ActionResult result = await rig.Action.ExecuteAsync(ctx, Whisper("{target}", "hello"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("recipient");
        rig.Whispers.ReceivedCalls().Should().BeEmpty();
        rig.Users.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task A_message_that_is_empty_after_templates_fails_and_sends_nothing()
    {
        Rig rig = Build(true, User("999", "viewer1", "Viewer1"));
        PipelineExecutionContext ctx = Context();
        ctx.Variables["note"] = "";

        ActionResult result = await rig.Action.ExecuteAsync(ctx, Whisper("viewer1", "{note}"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("message");
        rig.Whispers.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task A_new_recipient_past_the_daily_limit_fails_naming_the_limit_and_sends_nothing()
    {
        Rig rig = Build(true, User("999", "viewer1", "Viewer1"));
        for (int i = 0; i < WhisperRecipientCap.DailyNewRecipientLimit; i++)
            rig.Cap.TryAdmit($"filler-{i}").Should().BeTrue();

        ActionResult result = await rig.Action.ExecuteAsync(Context(), Whisper("viewer1", "hello"));

        result.Succeeded.Should().BeFalse();
        result
            .ErrorMessage.Should()
            .Contain(WhisperRecipientCap.DailyNewRecipientLimit.ToString())
            .And.Contain("limit");
        rig.Whispers.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task A_recipient_already_counted_today_still_gets_the_whisper_at_the_limit()
    {
        Rig rig = Build(true, User("999", "viewer1", "Viewer1"));
        rig.Cap.TryAdmit("999").Should().BeTrue();
        for (int i = 0; i < WhisperRecipientCap.DailyNewRecipientLimit - 1; i++)
            rig.Cap.TryAdmit($"filler-{i}").Should().BeTrue();

        ActionResult result = await rig.Action.ExecuteAsync(Context(), Whisper("viewer1", "again"));

        result.Succeeded.Should().BeTrue();
        await rig
            .Whispers.Received(1)
            .SendWhisperAsync(Broadcaster, "999", "again", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failed_twitch_call_passes_its_reason_through_as_a_failed_result()
    {
        Rig rig = Build(true, User("999", "viewer1", "Viewer1"));
        rig.Whispers.SendWhisperAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("The bot needs the user:manage:whispers scope.", "FORBIDDEN"));

        ActionResult result = await rig.Action.ExecuteAsync(Context(), Whisper("viewer1", "hello"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("The bot needs the user:manage:whispers scope.");
    }

    [Fact]
    public async Task Without_a_twitch_sender_the_action_fails_and_does_not_look_anyone_up()
    {
        Rig rig = Build(false, User("999", "viewer1", "Viewer1"));

        ActionResult result = await rig.Action.ExecuteAsync(Context(), Whisper("viewer1", "hello"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Twitch");
        rig.Whispers.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_twitch_user_lookup_fails_with_the_lookup_reason_and_sends_nothing()
    {
        Rig rig = Build(true);
        rig.Users.GetUsersByLoginsAsync(
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<IReadOnlyList<TwitchUser>>("Twitch is down.", "UPSTREAM"));

        ActionResult result = await rig.Action.ExecuteAsync(Context(), Whisper("viewer1", "hello"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Twitch is down.");
        rig.Whispers.ReceivedCalls().Should().BeEmpty();
    }
}
