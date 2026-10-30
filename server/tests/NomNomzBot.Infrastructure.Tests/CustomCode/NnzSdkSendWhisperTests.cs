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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.CustomCode.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.PipelineActions;
using NomNomzBot.Infrastructure.Chat.Whispers;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.CustomCode.Jint;
using NomNomzBot.Infrastructure.Platform.Messaging;
using NomNomzBot.Infrastructure.Tests.Chat;
using NomNomzBot.Infrastructure.Tests.Widgets;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// <c>nnz.api.actions.invoke('send_whisper', ...)</c> in a real script: the JS runs in the real executor, the call
/// goes through the real <see cref="ScriptActionInvoker"/> and the real owner-gated <see cref="OwnerActionService"/>
/// into the real <see cref="SendWhisperAction"/>. Only the owner's IAM, the rate limiter and the two Twitch API
/// clients are faked. The proof is what reached Twitch.
/// </summary>
public sealed class NnzSdkSendWhisperTests : IDisposable
{
    private const string ActionKey = "actions.invoke:send_whisper";
    private static readonly Guid Channel = Guid.Parse("0192c000-0000-7000-8000-0000000000b1");
    private static readonly Guid Owner = Guid.Parse("0192c000-0000-7000-8000-0000000000b3");

    private readonly WidgetSqliteTestDatabase _database = WidgetSqliteTestDatabase.Open();
    private readonly WidgetTestDbContext _db;
    private readonly ITwitchWhispersApi _whispers = Substitute.For<ITwitchWhispersApi>();
    private readonly IOwnerActionService _actions;

    public NnzSdkSendWhisperTests()
    {
        _db = _database.NewContext();
        _db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = Owner,
                TwitchChannelId = "123456789012",
                Name = "teststreamer",
                NameNormalized = "teststreamer",
                OverlayToken = Channel.ToString("N"),
            }
        );
        _db.SaveChanges();

        _whispers
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
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([]));
        users
            .GetUsersByLoginsAsync(
                Arg.Is<IReadOnlyList<string>>(logins => logins.Single() == "viewer1"),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success<IReadOnlyList<TwitchUser>>([
                    new(
                        Id: "999",
                        Login: "viewer1",
                        DisplayName: "Viewer1",
                        Type: "",
                        BroadcasterType: "",
                        Description: "",
                        ProfileImageUrl: "",
                        OfflineImageUrl: "",
                        ViewCount: 0,
                        CreatedAt: DateTimeOffset.UtcNow
                    ),
                ])
            );

        IActionAuthorizationService authorization = Substitute.For<IActionAuthorizationService>();
        authorization
            .AuthorizeActionAsync(Owner, Channel, "pipelines:write", Arg.Any<CancellationToken>())
            .Returns(Result.Success(true));
        IRateLimiterPartitionStore rateLimiter = Substitute.For<IRateLimiterPartitionStore>();
        rateLimiter
            .AcquireAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new RateLimitLease(true, 59, TimeSpan.Zero));

        SendWhisperAction action = new(
            TestTemplateResolver.Create(),
            users,
            TestMentionTargets.LoginsOnly(),
            [new TwitchWhisperDirectMessageSender(_whispers)],
            new WhisperRecipientCap(TimeProvider.System)
        );
        _actions = new OwnerActionService(
            _db,
            new ServiceCollection().AddSingleton<ICommandAction>(action).BuildServiceProvider(),
            authorization,
            rateLimiter,
            Substitute.For<IEventBus>(),
            NullLogger<OwnerActionService>.Instance
        );
    }

    public void Dispose()
    {
        _db.Dispose();
        _database.Dispose();
    }

    private sealed class ActionBridge(IOwnerActionService actions) : IScriptHostBridge
    {
        public List<string> Keys { get; } = [];

        public HostImportDelegate Resolve(string capabilityKey) =>
            (key, args, ct) =>
            {
                Keys.Add(key);
                return new ScriptActionInvoker(Channel, actions)
                    .Invoke(ScriptActionInvoker.ActionTypeOf(key)!, args, ct)
                    .ResultJson;
            };
    }

    private static ScriptCapabilityGrant Grant(params string[] keys) =>
        new(
            Guid.NewGuid(),
            [.. keys.Select(k => new ScriptCapabilityDescriptor(k, "tos", "ff", true))]
        );

    private static async Task<ScriptExecutionOutcomeResult> Run(
        string js,
        ScriptCapabilityGrant grant,
        IScriptHostBridge bridge
    ) =>
        (
            await new JintScriptExecutor().ExecuteAsync(
                new(
                    "exec-1",
                    js,
                    "hash",
                    new("u1", "User", [], new Dictionary<string, string>()),
                    ScriptResourceBudget.Baseline with
                    {
                        WallClockMs = 30_000,
                    }
                ),
                grant,
                bridge
            )
        ).Value;

    private const string WhisperScript = """
        var r = nnz.api.actions.invoke(
            'send_whisper',
            { recipient: '{user.name}', message: 'Welcome {user.name}!' },
            { 'user.name': 'Viewer1' }
        );
        bot.setVar('ok', String(r.success));
        bot.setVar('out', String(r.output));
        """;

    [Fact]
    public async Task A_granted_script_whispers_the_resolved_user_the_rendered_message()
    {
        ActionBridge bridge = new(_actions);

        ScriptExecutionOutcomeResult r = await Run(WhisperScript, Grant(ActionKey), bridge);

        r.Outcome.Should().Be(ScriptExecutionOutcome.Success);
        r.VariablesOut["ok"].Should().Be("true");
        r.VariablesOut["out"].Should().Be("Welcome Viewer1!");
        bridge.Keys.Should().Equal(ActionKey);
        await _whispers
            .Received(1)
            .SendWhisperAsync(Channel, "999", "Welcome Viewer1!", Arg.Any<CancellationToken>());
        _whispers.ReceivedCalls().Should().ContainSingle();
    }

    [Fact]
    public async Task A_script_without_the_send_whisper_grant_is_denied_and_whispers_nobody()
    {
        ActionBridge bridge = new(_actions);

        ScriptExecutionOutcomeResult r = await Run(
            WhisperScript,
            Grant("actions.invoke:send_message"),
            bridge
        );

        r.Outcome.Should().Be(ScriptExecutionOutcome.Denied);
        bridge.Keys.Should().BeEmpty();
        _whispers.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task A_whisper_to_nobody_comes_back_to_the_script_as_a_failed_result()
    {
        ActionBridge bridge = new(_actions);

        ScriptExecutionOutcomeResult r = await Run(
            """
            var r = nnz.api.actions.invoke('send_whisper', { recipient: 'ghostuser', message: 'hi' });
            bot.setVar('ok', String(r.success));
            bot.setVar('err', String(r.error));
            """,
            Grant(ActionKey),
            bridge
        );

        r.VariablesOut["ok"].Should().Be("false");
        r.VariablesOut["err"].Should().Contain("ghostuser");
        _whispers.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Compile_declares_the_send_whisper_grant()
    {
        ScriptCompilation compilation = (
            await new JintScriptExecutor().CompileAsync(
                "nnz.api.actions.invoke('send_whisper', { recipient: 'viewer1', message: 'hi' });"
            )
        ).Value;

        compilation.DeclaredCapabilities.Should().BeEquivalentTo(ActionKey);
    }
}
