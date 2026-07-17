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
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Infrastructure.AutomationApi;
using NSubstitute;
using PipelineEntity = NomNomzBot.Domain.Commands.Entities.Pipeline;

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
    }

    private static Harness Build(bool rateLimited = false)
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
        ServiceCollection services = new();
        services.AddSingleton(engine);
        IServiceScopeFactory scopeFactory = services
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

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
                    ? new RateLimitLease(false, 0, TimeSpan.FromSeconds(17))
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

        AutomationCommandService service = new(
            db,
            scopeFactory,
            limiter,
            chat,
            whispers,
            NullLogger<AutomationCommandService>.Instance
        );
        return new Harness
        {
            Service = service,
            Db = db,
            Engine = engine,
            EngineCalled = engineCalled,
            Limiter = limiter,
            Chat = chat,
            Whispers = whispers,
        };
    }

    private static AutomationPrincipal Principal(
        IReadOnlyList<string>? scopes = null,
        IReadOnlyList<Guid>? allowlist = null
    ) =>
        new(Channel, Guid.NewGuid(), "deck-token", scopes ?? ["invoke", "read", "chat"], allowlist);

    private static void SeedPipeline(AutomationTestDbContext db, bool enabled = true)
    {
        db.Pipelines.Add(
            new PipelineEntity
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
            new AutomationInvokeRequest { PipelineId = PipelineId, Args = ["boom", "big"] }
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
            new AutomationInvokeRequest { PipelineId = PipelineId }
        );
        noScope.IsFailure.Should().BeTrue();
        noScope.ErrorCode.Should().Be("FORBIDDEN");

        Result<AutomationInvokeResult> offList = await h.Service.InvokePipelineAsync(
            Principal(allowlist: [Guid.NewGuid()]),
            new AutomationInvokeRequest { PipelineId = PipelineId }
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
            new AutomationInvokeRequest { PipelineId = PipelineId }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("RATE_LIMITED");
        result.ErrorDetail.Should().Be("17", "the Retry-After seconds ride the error detail");
        await h
            .Engine.DidNotReceive()
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Chat_scope_reaches_the_chat_provider_and_whispers_route_to_helix()
    {
        Harness h = Build();

        Result message = await h.Service.SendChatAsync(
            Principal(),
            new AutomationChatRequest { Text = "hello chat" }
        );
        message.IsSuccess.Should().BeTrue(message.ErrorMessage);
        await h
            .Chat.Received(1)
            .SendMessageAsync(Channel, "hello chat", Arg.Any<CancellationToken>());

        Result whisper = await h.Service.SendChatAsync(
            Principal(),
            new AutomationChatRequest { Text = "psst", WhisperToTwitchUserId = "viewer-9" }
        );
        whisper.IsSuccess.Should().BeTrue(whisper.ErrorMessage);
        await h
            .Whispers.Received(1)
            .SendWhisperAsync(Channel, "viewer-9", "psst", Arg.Any<CancellationToken>());

        Result noScope = await h.Service.SendChatAsync(
            Principal(scopes: ["read"]),
            new AutomationChatRequest { Text = "nope" }
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
            new PipelineEntity
            {
                Id = Guid.NewGuid(),
                BroadcasterId = Channel,
                Name = "disabled-one",
                IsEnabled = false,
            }
        );
        h.Db.Channels.Add(
            new Channel
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
            Principal(allowlist: [PipelineId])
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
}
