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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Music;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.AutomationApi;
using NomNomzBot.Infrastructure.Commands;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.AutomationApi;

/// <summary>
/// Proves the data-plane gates fire in order and rejected calls have NO side effect
/// (automation-api.md §3/D5/D8): an <c>invoke</c>-scoped token enqueues a manual-trigger run
/// attributed to the token name with the correlation id both returned and injected as a variable;
/// a token lacking the scope — or invoking outside its allowlist — is rejected and the engine never
/// runs; a <c>chat</c>-scoped send reaches <c>IChatProvider</c> (whisper reaches the whispers API);
/// a rate-limit denial answers RATE_LIMITED with the Retry-After hint and performs no side effect.
/// </summary>
public sealed class AutomationCommandServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f201");
    private static readonly Guid PipelineId = Guid.Parse("0192a000-0000-7000-8000-00000000f202");

    private sealed class Harness
    {
        public required AutomationCommandService Service { get; init; }
        public required AutomationTestDbContext Db { get; init; }
        public required IPipelineEngine Engine { get; init; }
        public required TaskCompletionSource<PipelineRequest> EngineCalled { get; init; }
        public required IRateLimiterPartitionStore Limiter { get; init; }
        public required IChatProvider Chat { get; init; }
        public required ITwitchWhispersApi Whispers { get; init; }
        public required IMusicService Music { get; init; }
        public required IMusicProviderManageApi MusicManageApi { get; init; }
        public required IActionAuthorizationService ActionAuthz { get; init; }
        public required IObsControlService Obs { get; init; }
        public required ICommandInvoker Invoker { get; init; }
        public required TaskCompletionSource<(
            BindingTarget Target,
            InvocationContext Context
        )> InvokerCalled { get; init; }
    }

    private static ICommandAction FakeAction(
        string type,
        IReadOnlyList<PipelineActionFieldDescriptor>? fields = null
    )
    {
        ICommandAction action = Substitute.For<ICommandAction>();
        action.ActionType.Returns(type);
        action.Fields.Returns(fields ?? []);
        return action;
    }

    private static Harness Build(
        bool rateLimited = false,
        IReadOnlyList<ICommandAction>? actions = null
    )
    {
        AutomationTestDbContext db = AutomationTestDbContext.New();

        TaskCompletionSource<PipelineRequest> engineCalled = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        IPipelineEngine engine = Substitute.For<IPipelineEngine>();
        engine
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                engineCalled.TrySetResult(ci.ArgAt<PipelineRequest>(0));
                return Task.FromResult(
                    new PipelineExecutionResult
                    {
                        ExecutionId = Guid.NewGuid().ToString(),
                        Outcome = PipelineOutcome.Completed,
                        Duration = TimeSpan.Zero,
                    }
                );
            });
        TaskCompletionSource<(BindingTarget Target, InvocationContext Context)> invokerCalled = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        IRateLimiterPartitionStore limiter = Substitute.For<IRateLimiterPartitionStore>();
        limiter
            .AcquireAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                rateLimited
                    ? new(false, 0, TimeSpan.FromSeconds(17))
                    : new RateLimitLease(true, 10, TimeSpan.Zero)
            );

        IChatProvider chat = Substitute.For<IChatProvider>();
        chat.SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        ITwitchWhispersApi whispers = Substitute.For<ITwitchWhispersApi>();
        whispers
            .SendWhisperAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        IMusicService music = Substitute.For<IMusicService>();
        IMusicProviderManageApi musicManageApi = Substitute.For<IMusicProviderManageApi>();
        IActionAuthorizationService actionAuthz = Substitute.For<IActionAuthorizationService>();
        actionAuthz
            .AuthorizeActionAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(true));

        IObsControlService obs = Substitute.For<IObsControlService>();

        // The real invoker runs the target, so what the tests assert is what the channel would see.
        ITemplateResolver templates = Substitute.For<ITemplateResolver>();
        templates
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => "resolved:" + call.Arg<string>());
        IBuiltinCommandCatalog catalog = Substitute.For<IBuiltinCommandCatalog>();
        catalog.Get(Arg.Any<string>()).Returns((IBuiltinCommand?)null);
        IChannelTwitchBotResolver bots = Substitute.For<IChannelTwitchBotResolver>();
        bots.ResolveAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(new ChannelTwitchBot("bot-9", "NomNomzBotLive"));
        CommandInvoker realInvoker = new(
            db,
            engine,
            Substitute.For<IOwnerActionService>(),
            bots,
            templates,
            chat,
            catalog,
            Substitute.For<IOutboundSanctionAccessor>()
        );
        // Records what the service hands the invoker, then lets the real invoker run it.
        ICommandInvoker invoker = Substitute.For<ICommandInvoker>();
        invoker
            .InvokeAsync(
                Arg.Any<Guid>(),
                Arg.Any<BindingTarget>(),
                Arg.Any<InvocationContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci =>
            {
                invokerCalled.TrySetResult(
                    (ci.ArgAt<BindingTarget>(1), ci.ArgAt<InvocationContext>(2))
                );
                return realInvoker.InvokeAsync(
                    ci.ArgAt<Guid>(0),
                    ci.ArgAt<BindingTarget>(1),
                    ci.ArgAt<InvocationContext>(2),
                    ci.ArgAt<CancellationToken>(3)
                );
            });
        ServiceCollection services = new();
        services.AddSingleton(engine);
        services.AddSingleton(invoker);
        foreach (ICommandAction action in actions ?? [])
            services.AddSingleton(action);
        IServiceScopeFactory scopeFactory = services
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        AutomationCommandService service = new(
            db,
            scopeFactory,
            limiter,
            chat,
            whispers,
            music,
            musicManageApi,
            actionAuthz,
            obs,
            TimeProvider.System,
            NullLogger<AutomationCommandService>.Instance
        );
        return new()
        {
            Service = service,
            Db = db,
            Engine = engine,
            EngineCalled = engineCalled,
            Limiter = limiter,
            Chat = chat,
            Whispers = whispers,
            Music = music,
            MusicManageApi = musicManageApi,
            ActionAuthz = actionAuthz,
            Obs = obs,
            Invoker = invoker,
            InvokerCalled = invokerCalled,
        };
    }

    private static AutomationPrincipal Principal(
        IReadOnlyList<string>? scopes = null,
        IReadOnlyList<BindingTarget>? allowlist = null
    ) =>
        new(Channel, Guid.NewGuid(), "deck-token", scopes ?? ["invoke", "read", "chat"], allowlist);

    private static BindingTarget PipelineTarget(Guid id) =>
        new(BindingTargetKind.Pipeline, id.ToString());

    private static Guid SeedTemplateCommand(AutomationTestDbContext db)
    {
        Guid id = Guid.NewGuid();
        db.Commands.Add(
            new()
            {
                Id = id,
                BroadcasterId = Channel,
                Name = "hug",
                NameNormalized = "hug",
                Tier = "template",
                TemplateResponses = ["hug {user}"],
                IsEnabled = true,
                CooldownSeconds = 60,
                UserCooldownSeconds = 60,
                MinPermissionLevel = 100,
            }
        );
        db.SaveChanges();
        return id;
    }

    private static void SeedPipeline(AutomationTestDbContext db, bool enabled = true)
    {
        db.Pipelines.Add(
            new()
            {
                Id = PipelineId,
                BroadcasterId = Channel,
                Name = "confetti",
                IsEnabled = enabled,
            }
        );
        db.SaveChanges();
    }

    [Fact]
    public async Task Invoke_enqueues_a_manual_run_attributed_to_the_token()
    {
        Harness h = Build();
        SeedPipeline(h.Db);

        Result<AutomationInvokeResult> result = await h.Service.InvokePipelineAsync(
            Principal(),
            new() { PipelineId = PipelineId, Args = ["boom", "big"] }
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Accepted.Should().BeTrue();
        result.Value.PipelineId.Should().Be(PipelineId);

        PipelineRequest run = await h.EngineCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        run.BroadcasterId.Should().Be(Channel);
        run.PipelineId.Should().Be(PipelineId);
        run.TriggeredByDisplayName.Should().Be("deck-token");
        run.RawMessage.Should().Be("boom big");
        run.InitialVariables["trigger.source"].Should().Be("automation");
        run.InitialVariables["automation.correlation_id"]
            .Should()
            .Be(result.Value.ExecutionId.ToString(), "the caller and the pipeline share the id");
    }

    [Fact]
    public async Task Invoke_without_the_scope_or_outside_the_allowlist_never_reaches_the_engine()
    {
        Harness h = Build();
        SeedPipeline(h.Db);

        Result<AutomationInvokeResult> noScope = await h.Service.InvokePipelineAsync(
            Principal(scopes: ["read"]),
            new() { PipelineId = PipelineId }
        );
        noScope.IsFailure.Should().BeTrue();
        noScope.ErrorCode.Should().Be("FORBIDDEN");

        Result<AutomationInvokeResult> offList = await h.Service.InvokePipelineAsync(
            Principal(allowlist: [PipelineTarget(Guid.NewGuid())]),
            new() { PipelineId = PipelineId }
        );
        offList.IsFailure.Should().BeTrue();
        offList.ErrorCode.Should().Be("FORBIDDEN");

        h.EngineCalled.Task.IsCompleted.Should().BeFalse("no execution was enqueued");
        await h
            .Engine.DidNotReceive()
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_rate_limited_invoke_carries_the_retry_hint_and_has_no_side_effect()
    {
        Harness h = Build(rateLimited: true);
        SeedPipeline(h.Db);

        Result<AutomationInvokeResult> result = await h.Service.InvokePipelineAsync(
            Principal(),
            new() { PipelineId = PipelineId }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("RATE_LIMITED");
        result.ErrorDetail.Should().Be("17", "the Retry-After seconds ride the error detail");
        await h
            .Engine.DidNotReceive()
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_command_target_runs_the_command_and_the_chat_gets_its_reply()
    {
        Harness h = Build();
        Guid commandId = SeedTemplateCommand(h.Db);

        Result<AutomationInvokeResult> result = await h.Service.InvokePipelineAsync(
            Principal(),
            new()
            {
                Kind = "Command",
                Id = commandId.ToString(),
                Args = ["to", "you"],
            }
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Accepted.Should().BeTrue();
        result.Value.PipelineId.Should().BeNull("a template command is not a pipeline run");
        result.Value.Kind.Should().Be("command");
        result.Value.TargetId.Should().Be(commandId.ToString());

        await WaitForChatAsync(h, "resolved:hug {user}");
    }

    [Fact]
    public async Task A_token_pinned_to_one_command_runs_it_and_nothing_else()
    {
        Harness h = Build();
        SeedPipeline(h.Db);
        Guid pinned = SeedTemplateCommand(h.Db);
        Guid other = SeedTemplateCommand(h.Db);
        AutomationPrincipal principal = Principal(
            allowlist: [new(BindingTargetKind.Command, pinned.ToString())]
        );

        Result<AutomationInvokeResult> ok = await h.Service.InvokePipelineAsync(
            principal,
            new() { Kind = "command", Id = pinned.ToString().ToUpperInvariant() }
        );
        ok.IsSuccess.Should().BeTrue(ok.ErrorMessage);
        await WaitForChatAsync(h, "resolved:hug {user}");
        h.Chat.ClearReceivedCalls();

        Result<AutomationInvokeResult> otherCommand = await h.Service.InvokePipelineAsync(
            principal,
            new() { Kind = "command", Id = other.ToString() }
        );
        Result<AutomationInvokeResult> pipeline = await h.Service.InvokePipelineAsync(
            principal,
            new() { Kind = "pipeline", Id = PipelineId.ToString() }
        );
        Result<AutomationInvokeResult> legacyPipeline = await h.Service.InvokePipelineAsync(
            principal,
            new() { PipelineId = PipelineId }
        );

        foreach (
            Result<AutomationInvokeResult> denied in new[]
            {
                otherCommand,
                pipeline,
                legacyPipeline,
            }
        )
        {
            denied.IsFailure.Should().BeTrue();
            denied.ErrorCode.Should().Be("FORBIDDEN");
            denied.ErrorMessage.Should().Be("This token is not allowed to invoke that target.");
        }

        await Task.Delay(200);
        await h
            .Chat.DidNotReceive()
            .SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await h
            .Engine.DidNotReceive()
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_pipeline_target_by_kind_runs_the_pipeline_and_an_unpinned_token_may_run_any_kind()
    {
        Harness h = Build();
        SeedPipeline(h.Db);

        Result<AutomationInvokeResult> result = await h.Service.InvokePipelineAsync(
            Principal(),
            new()
            {
                Kind = "pipeline",
                Id = PipelineId.ToString(),
                Args = ["go"],
            }
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.PipelineId.Should().Be(PipelineId);
        result.Value.Kind.Should().Be("pipeline");
        PipelineRequest run = await h.EngineCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        run.PipelineId.Should().Be(PipelineId);
        run.InitialVariables["automation.correlation_id"]
            .Should()
            .Be(result.Value.ExecutionId.ToString());
    }

    [Fact]
    public async Task A_malformed_or_missing_target_is_refused_without_side_effect()
    {
        Harness h = Build();
        SeedPipeline(h.Db);

        Result<AutomationInvokeResult> badKind = await h.Service.InvokePipelineAsync(
            Principal(),
            new() { Kind = "widget", Id = "x" }
        );
        Result<AutomationInvokeResult> badPipelineId = await h.Service.InvokePipelineAsync(
            Principal(),
            new() { Kind = "pipeline", Id = "not-a-guid" }
        );
        Result<AutomationInvokeResult> missingCommand = await h.Service.InvokePipelineAsync(
            Principal(),
            new() { Kind = "command", Id = Guid.NewGuid().ToString() }
        );

        badKind.ErrorCode.Should().Be("VALIDATION_FAILED");
        badPipelineId.ErrorCode.Should().Be("VALIDATION_FAILED");
        missingCommand.ErrorCode.Should().Be("NOT_FOUND");
        await h
            .Engine.DidNotReceive()
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void The_allowlist_matches_by_kind_and_id_ignoring_case_and_actions_by_action_type()
    {
        AutomationPrincipal principal = Principal(
            allowlist:
            [
                new(BindingTargetKind.Command, "Hug"),
                new(BindingTargetKind.Action, ActionType: "send_message"),
            ]
        );
        AutomationPrincipal unpinned = Principal();
        AutomationPrincipal emptyList = Principal(allowlist: []);

        principal.Allows(new(BindingTargetKind.Command, "hug")).Should().BeTrue();
        principal
            .Allows(new(BindingTargetKind.Pipeline, "hug"))
            .Should()
            .BeFalse("the kind differs");
        principal.Allows(new(BindingTargetKind.Command, "wave")).Should().BeFalse();
        principal
            .Allows(new(BindingTargetKind.Action, "other-id", "SEND_MESSAGE"))
            .Should()
            .BeTrue("an action is matched by its type");
        principal.Allows(new(BindingTargetKind.Action, ActionType: "timeout")).Should().BeFalse();
        unpinned.Allows(new(BindingTargetKind.Command, "anything")).Should().BeTrue();
        emptyList.Allows(new(BindingTargetKind.Pipeline, "anything")).Should().BeTrue();
    }

    private static async Task WaitForChatAsync(Harness h, string message)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (
                h
                    .Chat.ReceivedCalls()
                    .Any(c =>
                        c.GetMethodInfo().Name == nameof(IChatProvider.SendMessageAsync)
                        && Equals(c.GetArguments()[1], message)
                    )
            )
                return;

            await Task.Delay(50);
        }

        throw new TimeoutException($"Chat never received '{message}'.");
    }

    [Fact]
    public async Task Chat_scope_reaches_the_chat_provider_and_whispers_route_to_helix()
    {
        Harness h = Build();

        Result message = await h.Service.SendChatAsync(Principal(), new() { Text = "hello chat" });
        message.IsSuccess.Should().BeTrue(message.ErrorMessage);
        await h
            .Chat.Received(1)
            .SendMessageAsync(Channel, "hello chat", Arg.Any<CancellationToken>());

        Result whisper = await h.Service.SendChatAsync(
            Principal(),
            new() { Text = "psst", WhisperToTwitchUserId = "viewer-9" }
        );
        whisper.IsSuccess.Should().BeTrue(whisper.ErrorMessage);
        await h
            .Whispers.Received(1)
            .SendWhisperAsync(Channel, "viewer-9", "psst", Arg.Any<CancellationToken>());

        Result noScope = await h.Service.SendChatAsync(
            Principal(scopes: ["read"]),
            new() { Text = "nope" }
        );
        noScope.IsFailure.Should().BeTrue();
        noScope.ErrorCode.Should().Be("FORBIDDEN");
        await h
            .Chat.Received(1) // still just the one send from above
            .SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reads_honor_scope_allowlist_and_tenant()
    {
        Harness h = Build();
        SeedPipeline(h.Db);
        h.Db.Pipelines.Add(
            new()
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Channel,
                Name = "disabled-one",
                IsEnabled = false,
            }
        );
        h.Db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = Guid.NewGuid(),
                Name = "stoney",
                NameNormalized = "stoney",
            }
        );
        h.Db.SaveChanges();

        // The allowlisted token sees only its allowed pipeline; disabled ones never appear.
        Result<IReadOnlyList<AutomationPipelineRef>> pipelines = await h.Service.ListPipelinesAsync(
            Principal(allowlist: [PipelineTarget(PipelineId)])
        );
        pipelines.IsSuccess.Should().BeTrue(pipelines.ErrorMessage);
        pipelines.Value.Should().ContainSingle(p => p.Id == PipelineId && p.Name == "confetti");

        Result<AutomationInfo> info = await h.Service.GetInfoAsync(Principal());
        info.IsSuccess.Should().BeTrue(info.ErrorMessage);
        info.Value.ChannelName.Should().Be("stoney");

        Result<AutomationInfo> noScope = await h.Service.GetInfoAsync(
            Principal(scopes: ["invoke"])
        );
        noScope.IsFailure.Should().BeTrue();
        noScope.ErrorCode.Should().Be("FORBIDDEN");
    }

    // ─── music-automation-controls.md §3.2: now-playing / devices / playlists reads ───

    [Fact]
    public async Task GetNowPlaying_maps_the_active_providers_current_track_and_saved_state()
    {
        Harness h = Build();
        h.Music.GetActiveProviderKeyAsync(Channel.ToString(), Arg.Any<CancellationToken>())
            .Returns("spotify");
        h.Music.GetNowPlayingAsync(Channel.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new NowPlaying(
                    "Song",
                    "Artist",
                    "Album",
                    null,
                    200_000,
                    10_000,
                    true,
                    100,
                    null,
                    "spotify",
                    "spotify:track:1",
                    true,
                    MusicRepeatMode.Track,
                    "spotify:artist:1"
                )
            );
        h.MusicManageApi.AreTracksSavedAsync(
                Channel,
                "spotify",
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success<IReadOnlyList<bool>>([true]));

        Result<AutomationNowPlayingDto> result = await h.Service.GetNowPlayingAsync(Principal());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Title.Should().Be("Song");
        result.Value.IsPlaying.Should().BeTrue();
        result.Value.ShuffleEnabled.Should().BeTrue();
        result.Value.RepeatMode.Should().Be("track");
        result.Value.IsSaved.Should().BeTrue();
    }

    [Fact]
    public async Task GetNowPlaying_fails_typed_when_no_provider_is_connected()
    {
        Harness h = Build();
        h.Music.GetActiveProviderKeyAsync(Channel.ToString(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        Result<AutomationNowPlayingDto> result = await h.Service.GetNowPlayingAsync(Principal());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("CAPABILITY_UNSUPPORTED");
    }

    [Fact]
    public async Task GetNowPlaying_requires_the_read_scope()
    {
        Harness h = Build();

        Result<AutomationNowPlayingDto> result = await h.Service.GetNowPlayingAsync(
            Principal(scopes: ["invoke"])
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("FORBIDDEN");
        await h
            .Music.DidNotReceive()
            .GetActiveProviderKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetDevices_maps_the_music_services_device_list()
    {
        Harness h = Build();
        h.Music.GetDevicesAsync(Channel.ToString(), Arg.Any<CancellationToken>())
            .Returns([new("dev-1", "Laptop", "Computer", true, 80)]);

        Result<IReadOnlyList<AutomationDeviceDto>> result = await h.Service.GetDevicesAsync(
            Principal()
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result
            .Value.Should()
            .ContainSingle(d => d.Id == "dev-1" && d.Name == "Laptop" && d.IsActive);
    }

    // ─── music-automation-controls.md D5: music:control:write invoke gate ─────

    [Fact]
    public async Task Invoke_rejects_a_music_pipeline_when_the_tokens_creator_lost_music_control()
    {
        Harness h = Build();
        SeedPipeline(h.Db);
        Guid tokenId = Guid.NewGuid();
        h.Db.PipelineSteps.Add(
            new()
            {
                Id = Guid.NewGuid(),
                PipelineId = PipelineId,
                BroadcasterId = Channel,
                Order = 0,
                ActionType = "music_play_pause",
            }
        );
        h.Db.AutomationApiTokens.Add(
            new()
            {
                Id = tokenId,
                BroadcasterId = Channel,
                Name = "deck-token",
                TokenHash = "hash",
                TokenPrefix = "nnzb_ak_test",
                ScopesJson = "[\"invoke\"]",
                CreatedByUserId = Guid.NewGuid(),
            }
        );
        h.Db.SaveChanges();
        h.ActionAuthz.AuthorizeActionAsync(
                Arg.Any<Guid>(),
                Channel,
                "music:control:write",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(false));

        Result<AutomationInvokeResult> result = await h.Service.InvokePipelineAsync(
            new(Channel, tokenId, "deck-token", ["invoke"], null),
            new() { PipelineId = PipelineId }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("FORBIDDEN");
    }

    // ─── Stream Deck keys name an action target; no wrapper pipeline exists ───

    private static Guid SeedCreatorToken(AutomationTestDbContext db)
    {
        Guid tokenId = Guid.NewGuid();
        db.AutomationApiTokens.Add(
            new()
            {
                Id = tokenId,
                BroadcasterId = Channel,
                Name = "deck-token",
                TokenHash = "hash",
                TokenPrefix = "nnzb_ak_test",
                ScopesJson = "[\"invoke\"]",
                CreatedByUserId = Guid.NewGuid(),
            }
        );
        db.SaveChanges();
        return tokenId;
    }

    private static void AssertInvokerNeverCalled(Harness h) =>
        _ = h
            .Invoker.DidNotReceive()
            .InvokeAsync(
                Arg.Any<Guid>(),
                Arg.Any<BindingTarget>(),
                Arg.Any<InvocationContext>(),
                Arg.Any<CancellationToken>()
            );

    [Fact]
    public async Task Invoking_an_action_name_runs_an_action_target_with_the_keys_settings_as_variables()
    {
        Harness h = Build(
            actions:
            [
                FakeAction(
                    "obs_switch_scene",
                    [new("scene", PipelineActionFieldKind.Text, Required: true)]
                ),
            ]
        );
        Guid tokenId = SeedCreatorToken(h.Db);

        Result<AutomationInvokeResult> result = await h.Service.InvokePipelineAsync(
            new(Channel, tokenId, "deck-token", ["invoke"], null),
            new()
            {
                PipelineName = "obs_switch_scene",
                Variables = new Dictionary<string, string> { ["scene"] = "BRB" },
            }
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Accepted.Should().BeTrue();
        result.Value.PipelineId.Should().BeNull("an action is not a pipeline");
        result.Value.Kind.Should().Be("action");
        result.Value.TargetId.Should().Be("obs_switch_scene");

        (BindingTarget target, InvocationContext context) = await h.InvokerCalled.Task.WaitAsync(
            TimeSpan.FromSeconds(5)
        );
        target.Kind.Should().Be(BindingTargetKind.Action);
        target.ActionType.Should().Be("obs_switch_scene");
        target.Params!["scene"].GetString().Should().Be("{scene}");
        context.UserId.Should().BeNull();
        context.TriggerSource.Should().Be("automation");
        context.Variables!["scene"].Should().Be("BRB");
        context
            .Variables["automation.correlation_id"]
            .Should()
            .Be(result.Value.ExecutionId.ToString());
        h.Db.Pipelines.Should().BeEmpty("a key must not create a wrapper pipeline");
        await h
            .Engine.DidNotReceive()
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_real_pipeline_named_like_an_action_still_runs_through_the_engine()
    {
        Harness h = Build(actions: [FakeAction("music_play")]);
        h.Db.Pipelines.Add(
            new()
            {
                Id = PipelineId,
                BroadcasterId = Channel,
                Name = "music_play",
                IsEnabled = true,
            }
        );
        h.Db.SaveChanges();

        Result<AutomationInvokeResult> result = await h.Service.InvokePipelineAsync(
            Principal(),
            new() { PipelineName = "music_play" }
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.PipelineId.Should().Be(PipelineId);
        PipelineRequest run = await h.EngineCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        run.PipelineId.Should().Be(PipelineId);
        (BindingTarget target, InvocationContext _) = await h.InvokerCalled.Task.WaitAsync(
            TimeSpan.FromSeconds(5)
        );
        target
            .Kind.Should()
            .Be(BindingTargetKind.Pipeline, "the name matched a real pipeline, not the action");
    }

    [Fact]
    public async Task An_action_outside_the_stream_deck_families_is_not_found()
    {
        Harness h = Build(actions: [FakeAction("chat_send")]);

        Result<AutomationInvokeResult> result = await h.Service.InvokePipelineAsync(
            Principal(),
            new() { PipelineName = "chat_send" }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
        AssertInvokerNeverCalled(h);
    }

    [Fact]
    public async Task An_allowlisted_token_cannot_run_an_action_target()
    {
        Harness h = Build(actions: [FakeAction("obs_streaming")]);

        Result<AutomationInvokeResult> result = await h.Service.InvokePipelineAsync(
            Principal(allowlist: [PipelineTarget(PipelineId)]),
            new() { PipelineName = "obs_streaming" }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("FORBIDDEN");
        AssertInvokerNeverCalled(h);
    }

    [Fact]
    public async Task A_music_action_needs_the_creator_to_still_hold_music_control()
    {
        Harness h = Build(actions: [FakeAction("music_play")]);
        Guid tokenId = SeedCreatorToken(h.Db);
        h.ActionAuthz.AuthorizeActionAsync(
                Arg.Any<Guid>(),
                Channel,
                "music:control:write",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(false));

        Result<AutomationInvokeResult> result = await h.Service.InvokePipelineAsync(
            new(Channel, tokenId, "deck-token", ["invoke"], null),
            new() { PipelineName = "music_play" }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("FORBIDDEN");
        AssertInvokerNeverCalled(h);
    }

    [Fact]
    public async Task A_rate_limited_action_invoke_runs_nothing()
    {
        Harness h = Build(rateLimited: true, actions: [FakeAction("obs_streaming")]);

        Result<AutomationInvokeResult> result = await h.Service.InvokePipelineAsync(
            Principal(),
            new() { PipelineName = "obs_streaming" }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("RATE_LIMITED");
        AssertInvokerNeverCalled(h);
    }

    [Fact]
    public async Task Invoke_allows_a_non_music_pipeline_regardless_of_music_control()
    {
        Harness h = Build();
        SeedPipeline(h.Db);
        h.ActionAuthz.AuthorizeActionAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                "music:control:write",
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(false));

        Result<AutomationInvokeResult> result = await h.Service.InvokePipelineAsync(
            Principal(),
            new() { PipelineId = PipelineId }
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public async Task GetPlaylists_maps_the_music_services_playlist_page()
    {
        Harness h = Build();
        h.Music.GetPlaylistsAsync(Channel.ToString(), 0, 20, Arg.Any<CancellationToken>())
            .Returns([new("pl-1", "Chill", "spotify:playlist:pl-1", 12, null, "spotify")]);

        Result<IReadOnlyList<AutomationPlaylistDto>> result = await h.Service.GetPlaylistsAsync(
            Principal(),
            limit: 20,
            offset: 0
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().ContainSingle(p => p.Id == "pl-1" && p.TrackCount == 12);
    }

    // ─── S-STREAMDECK-OBS-REMAINDER: scene/input read surface for the Stream Deck picker ─────

    [Fact]
    public async Task GetObsScenes_maps_the_obs_control_services_scene_list()
    {
        Harness h = Build();
        h.Obs.GetScenesAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<ObsSceneDto>>([
                    new("Starting Soon", false),
                    new("Game Scene", true),
                ])
            );

        Result<IReadOnlyList<AutomationObsSceneDto>> result = await h.Service.GetObsScenesAsync(
            Principal()
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().HaveCount(2);
        result.Value.Should().ContainSingle(s => s.Name == "Game Scene" && s.IsCurrent);
        result.Value.Should().ContainSingle(s => s.Name == "Starting Soon" && !s.IsCurrent);
    }

    [Fact]
    public async Task GetObsScenes_rejects_a_token_without_the_read_scope_and_never_calls_obs()
    {
        Harness h = Build();

        Result<IReadOnlyList<AutomationObsSceneDto>> result = await h.Service.GetObsScenesAsync(
            Principal(scopes: ["invoke"])
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("FORBIDDEN");
        await h.Obs.DidNotReceive().GetScenesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetObsScenes_propagates_an_OBS_not_connected_failure_as_is()
    {
        Harness h = Build();
        h.Obs.GetScenesAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<IReadOnlyList<ObsSceneDto>>(
                    "OBS is not connected.",
                    "OBS_NOT_CONNECTED"
                )
            );

        Result<IReadOnlyList<AutomationObsSceneDto>> result = await h.Service.GetObsScenesAsync(
            Principal()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("OBS_NOT_CONNECTED");
    }

    [Fact]
    public async Task GetObsInputs_maps_the_obs_control_services_input_list()
    {
        Harness h = Build();
        h.Obs.GetInputsAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<ObsInputDto>>([
                    new("Mic/Aux", "wasapi_input_capture", false, -6.0),
                ])
            );

        Result<IReadOnlyList<AutomationObsInputDto>> result = await h.Service.GetObsInputsAsync(
            Principal()
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result
            .Value.Should()
            .ContainSingle(i =>
                i.Name == "Mic/Aux"
                && i.Kind == "wasapi_input_capture"
                && i.Muted == false
                && i.VolumeDb == -6.0
            );
    }

    [Fact]
    public async Task GetObsInputs_rejects_a_token_without_the_read_scope_and_never_calls_obs()
    {
        Harness h = Build();

        Result<IReadOnlyList<AutomationObsInputDto>> result = await h.Service.GetObsInputsAsync(
            Principal(scopes: ["invoke"])
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("FORBIDDEN");
        await h.Obs.DidNotReceive().GetInputsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_rate_limited_GetObsScenes_carries_the_retry_hint_and_never_calls_obs()
    {
        Harness h = Build(rateLimited: true);

        Result<IReadOnlyList<AutomationObsSceneDto>> result = await h.Service.GetObsScenesAsync(
            Principal()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("RATE_LIMITED");
        result.ErrorDetail.Should().Be("17", "the Retry-After seconds ride the error detail");
        await h.Obs.DidNotReceive().GetScenesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetObsSceneItems_maps_the_obs_control_services_scene_item_list()
    {
        Harness h = Build();
        h.Obs.GetSceneItemListAsync(Channel, "Game Scene", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<ObsSceneItemDto>>([
                    new(1, "Webcam", true, false),
                    new(2, "Overlay", false, false),
                ])
            );

        Result<IReadOnlyList<AutomationObsSceneItemDto>> result =
            await h.Service.GetObsSceneItemsAsync(Principal(), "Game Scene");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().HaveCount(2);
        result
            .Value.Should()
            .ContainSingle(i => i.SourceName == "Webcam" && i.SceneItemId == 1 && i.Enabled);
        result
            .Value.Should()
            .ContainSingle(i => i.SourceName == "Overlay" && i.SceneItemId == 2 && !i.Enabled);
    }

    [Fact]
    public async Task GetObsSceneItems_rejects_a_token_without_the_read_scope_and_never_calls_obs()
    {
        Harness h = Build();

        Result<IReadOnlyList<AutomationObsSceneItemDto>> result =
            await h.Service.GetObsSceneItemsAsync(Principal(scopes: ["invoke"]), "Game Scene");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("FORBIDDEN");
        await h
            .Obs.DidNotReceive()
            .GetSceneItemListAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GetObsSceneItems_propagates_an_OBS_not_connected_failure_as_is()
    {
        Harness h = Build();
        h.Obs.GetSceneItemListAsync(Channel, "Game Scene", Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<IReadOnlyList<ObsSceneItemDto>>(
                    "OBS is not connected.",
                    "OBS_NOT_CONNECTED"
                )
            );

        Result<IReadOnlyList<AutomationObsSceneItemDto>> result =
            await h.Service.GetObsSceneItemsAsync(Principal(), "Game Scene");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("OBS_NOT_CONNECTED");
    }

    [Fact]
    public async Task GetObsSceneTransitions_maps_the_obs_control_services_transition_list()
    {
        Harness h = Build();
        h.Obs.GetSceneTransitionListAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<ObsTransitionDto>>([
                    new("Fade", true),
                    new("Cut", false),
                ])
            );

        Result<IReadOnlyList<AutomationObsTransitionDto>> result =
            await h.Service.GetObsSceneTransitionsAsync(Principal());

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().HaveCount(2);
        result.Value.Should().ContainSingle(t => t.Name == "Fade" && t.IsCurrent);
        result.Value.Should().ContainSingle(t => t.Name == "Cut" && !t.IsCurrent);
    }

    [Fact]
    public async Task GetObsSceneTransitions_rejects_a_token_without_the_read_scope_and_never_calls_obs()
    {
        Harness h = Build();

        Result<IReadOnlyList<AutomationObsTransitionDto>> result =
            await h.Service.GetObsSceneTransitionsAsync(Principal(scopes: ["invoke"]));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("FORBIDDEN");
        await h
            .Obs.DidNotReceive()
            .GetSceneTransitionListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetObsSceneTransitions_propagates_an_OBS_not_connected_failure_as_is()
    {
        Harness h = Build();
        h.Obs.GetSceneTransitionListAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<IReadOnlyList<ObsTransitionDto>>(
                    "OBS is not connected.",
                    "OBS_NOT_CONNECTED"
                )
            );

        Result<IReadOnlyList<AutomationObsTransitionDto>> result =
            await h.Service.GetObsSceneTransitionsAsync(Principal());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("OBS_NOT_CONNECTED");
    }

    [Fact]
    public async Task GetObsSourceFilters_maps_the_obs_control_services_filter_list()
    {
        Harness h = Build();
        h.Obs.GetSourceFilterListAsync(Channel, "Webcam", Arg.Any<CancellationToken>())
            .Returns(
                Result.Success<IReadOnlyList<ObsFilterDto>>([
                    new("Color Correction", "color_filter", true, 0),
                ])
            );

        Result<IReadOnlyList<AutomationObsFilterDto>> result =
            await h.Service.GetObsSourceFiltersAsync(Principal(), "Webcam");

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result
            .Value.Should()
            .ContainSingle(f =>
                f.Name == "Color Correction"
                && f.Kind == "color_filter"
                && f.Enabled
                && f.Index == 0
            );
    }

    [Fact]
    public async Task GetObsSourceFilters_rejects_a_token_without_the_read_scope_and_never_calls_obs()
    {
        Harness h = Build();

        Result<IReadOnlyList<AutomationObsFilterDto>> result =
            await h.Service.GetObsSourceFiltersAsync(Principal(scopes: ["invoke"]), "Webcam");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("FORBIDDEN");
        await h
            .Obs.DidNotReceive()
            .GetSourceFilterListAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GetObsSourceFilters_propagates_an_OBS_not_connected_failure_as_is()
    {
        Harness h = Build();
        h.Obs.GetSourceFilterListAsync(Channel, "Webcam", Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<IReadOnlyList<ObsFilterDto>>(
                    "OBS is not connected.",
                    "OBS_NOT_CONNECTED"
                )
            );

        Result<IReadOnlyList<AutomationObsFilterDto>> result =
            await h.Service.GetObsSourceFiltersAsync(Principal(), "Webcam");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("OBS_NOT_CONNECTED");
    }
}
