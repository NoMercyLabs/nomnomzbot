// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.AutomationApi.Services;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Entities;
using PipelineEntity = NomNomzBot.Domain.Commands.Entities.Pipeline;

namespace NomNomzBot.Infrastructure.AutomationApi;

/// <summary>
/// The data-plane command surface (automation-api.md §3/§4.1/D5). Order of gates on every call:
/// scope → (invoke) allowlist → per-token rate limit — a rejected call performs no side effect and a
/// rate-limit denial carries the retry-after hint for the 429. Invocation is fire-and-forget: the
/// pipeline runs on a fresh DI scope in the background, attributed to the token name, with a minted
/// correlation id the caller gets back and the pipeline sees as <c>{{automation.correlation_id}}</c>.
/// </summary>
public class AutomationCommandService : IAutomationCommandService
{
    private const int InvokePerMinute = 60;
    private const int ChatPerMinute = 20;
    private const int ReadsPerMinute = 120;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly IApplicationDbContext _db;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRateLimiterPartitionStore _rateLimiter;
    private readonly IChatProvider _chat;
    private readonly ITwitchWhispersApi _whispers;
    private readonly ILogger<AutomationCommandService> _logger;

    public AutomationCommandService(
        IApplicationDbContext db,
        IServiceScopeFactory scopeFactory,
        IRateLimiterPartitionStore rateLimiter,
        IChatProvider chat,
        ITwitchWhispersApi whispers,
        ILogger<AutomationCommandService> logger
    )
    {
        _db = db;
        _scopeFactory = scopeFactory;
        _rateLimiter = rateLimiter;
        _chat = chat;
        _whispers = whispers;
        _logger = logger;
    }

    public async Task<Result<AutomationInvokeResult>> InvokePipelineAsync(
        AutomationPrincipal principal,
        AutomationInvokeRequest request,
        CancellationToken ct = default
    )
    {
        if (!principal.Scopes.Contains("invoke"))
            return Forbidden<AutomationInvokeResult>("invoke");

        PipelineEntity? pipeline = await ResolvePipelineAsync(principal, request, ct);
        if (pipeline is null)
            return Errors.NotFound<AutomationInvokeResult>(
                "Pipeline",
                request.PipelineId?.ToString() ?? request.PipelineName ?? "(unspecified)"
            );
        if (
            principal.AllowedPipelineIds is { Count: > 0 } allowlist
            && !allowlist.Contains(pipeline.Id)
        )
            return Result.Failure<AutomationInvokeResult>(
                "This token is not allowed to invoke that pipeline.",
                "FORBIDDEN"
            );

        Result limited = await AcquireAsync(principal, "invoke", InvokePerMinute, ct);
        if (limited.IsFailure)
            return Result.Failure<AutomationInvokeResult>(
                limited.ErrorMessage!,
                limited.ErrorCode!,
                limited.ErrorDetail
            );

        Guid correlationId = Guid.CreateVersion7();
        Dictionary<string, string> variables = request.Variables is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(request.Variables);
        variables["trigger.source"] = "automation";
        variables["automation.correlation_id"] = correlationId.ToString();

        PipelineRequest run = new()
        {
            BroadcasterId = principal.BroadcasterId,
            PipelineId = pipeline.Id,
            TriggeredByUserId = principal.TokenId.ToString(),
            TriggeredByDisplayName = principal.TokenName,
            RawMessage = request.Args is { Count: > 0 } args ? string.Join(' ', args) : "",
            InitialVariables = variables,
        };

        // Fire-and-forget on a fresh scope (D5): the API answers "accepted", it never blocks on —
        // or reports — pipeline completion.
        _ = Task.Run(
            async () =>
            {
                using IServiceScope scope = _scopeFactory.CreateScope();
                try
                {
                    IPipelineEngine engine =
                        scope.ServiceProvider.GetRequiredService<IPipelineEngine>();
                    await engine.ExecuteAsync(run, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Automation-invoked pipeline {Pipeline} failed for {Channel} (correlation {Correlation}).",
                        run.PipelineId,
                        run.BroadcasterId,
                        correlationId
                    );
                }
            },
            CancellationToken.None
        );

        return Result.Success(
            new AutomationInvokeResult(pipeline.Id, correlationId, Accepted: true)
        );
    }

    public async Task<Result<IReadOnlyList<AutomationPipelineRef>>> ListPipelinesAsync(
        AutomationPrincipal principal,
        CancellationToken ct = default
    )
    {
        if (!principal.Scopes.Contains("read"))
            return Forbidden<IReadOnlyList<AutomationPipelineRef>>("read");
        Result limited = await AcquireAsync(principal, "read", ReadsPerMinute, ct);
        if (limited.IsFailure)
            return Result.Failure<IReadOnlyList<AutomationPipelineRef>>(
                limited.ErrorMessage!,
                limited.ErrorCode!,
                limited.ErrorDetail
            );

        List<PipelineEntity> pipelines = await _db
            .Pipelines.Where(p => p.BroadcasterId == principal.BroadcasterId && p.IsEnabled)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);
        IReadOnlyList<AutomationPipelineRef> refs =
        [
            .. pipelines
                .Where(p =>
                    principal.AllowedPipelineIds is not { Count: > 0 } allowlist
                    || allowlist.Contains(p.Id)
                )
                .Select(p => new AutomationPipelineRef(p.Id, p.Name)),
        ];
        return Result.Success(refs);
    }

    public async Task<Result<IReadOnlyList<AutomationCommandRef>>> ListCommandsAsync(
        AutomationPrincipal principal,
        CancellationToken ct = default
    )
    {
        if (!principal.Scopes.Contains("read"))
            return Forbidden<IReadOnlyList<AutomationCommandRef>>("read");
        Result limited = await AcquireAsync(principal, "read", ReadsPerMinute, ct);
        if (limited.IsFailure)
            return Result.Failure<IReadOnlyList<AutomationCommandRef>>(
                limited.ErrorMessage!,
                limited.ErrorCode!,
                limited.ErrorDetail
            );

        List<AutomationCommandRef> commands = await _db
            .Commands.Where(c => c.BroadcasterId == principal.BroadcasterId && c.IsEnabled)
            .OrderBy(c => c.Name)
            .Select(c => new AutomationCommandRef(c.Name, c.Aliases))
            .ToListAsync(ct);
        return Result.Success<IReadOnlyList<AutomationCommandRef>>(commands);
    }

    public async Task<Result<AutomationInfo>> GetInfoAsync(
        AutomationPrincipal principal,
        CancellationToken ct = default
    )
    {
        if (!principal.Scopes.Contains("read"))
            return Forbidden<AutomationInfo>("read");
        Result limited = await AcquireAsync(principal, "read", ReadsPerMinute, ct);
        if (limited.IsFailure)
            return Result.Failure<AutomationInfo>(
                limited.ErrorMessage!,
                limited.ErrorCode!,
                limited.ErrorDetail
            );

        Channel? channel = await _db.Channels.FirstOrDefaultAsync(
            c => c.Id == principal.BroadcasterId,
            ct
        );
        if (channel is null)
            return Errors.NotFound<AutomationInfo>("Channel", principal.BroadcasterId.ToString());
        return Result.Success(
            new AutomationInfo(channel.Id, channel.Name, channel.Provider, ApiVersion: "1")
        );
    }

    public async Task<Result> SendChatAsync(
        AutomationPrincipal principal,
        AutomationChatRequest request,
        CancellationToken ct = default
    )
    {
        if (!principal.Scopes.Contains("chat"))
            return Result.Failure("This token lacks the 'chat' scope.", "FORBIDDEN");
        if (string.IsNullOrWhiteSpace(request.Text))
            return Errors.ValidationFailed("Nothing to send.");

        Result limited = await AcquireAsync(principal, "chat", ChatPerMinute, ct);
        if (limited.IsFailure)
            return limited;

        if (request.WhisperToTwitchUserId is string whisperTo)
            return await _whispers.SendWhisperAsync(
                principal.BroadcasterId,
                whisperTo,
                request.Text,
                ct
            );

        if (request.ReplyToMessageId is string replyTo)
        {
            await _chat.SendReplyAsync(principal.BroadcasterId, replyTo, request.Text, ct);
            return Result.Success();
        }

        bool sent = await _chat.SendMessageAsync(principal.BroadcasterId, request.Text, ct);
        return sent
            ? Result.Success()
            : Result.Failure("The chat message could not be sent.", "SERVICE_UNAVAILABLE");
    }

    private async Task<PipelineEntity?> ResolvePipelineAsync(
        AutomationPrincipal principal,
        AutomationInvokeRequest request,
        CancellationToken ct
    )
    {
        if (request.PipelineId is Guid id)
            return await _db.Pipelines.FirstOrDefaultAsync(
                p => p.BroadcasterId == principal.BroadcasterId && p.Id == id && p.IsEnabled,
                ct
            );
        if (!string.IsNullOrWhiteSpace(request.PipelineName))
            return await _db.Pipelines.FirstOrDefaultAsync(
                p =>
                    p.BroadcasterId == principal.BroadcasterId
                    && p.Name == request.PipelineName
                    && p.IsEnabled,
                ct
            );
        return null;
    }

    /// <summary>Per-token per-operation window bucket; the denial carries the Retry-After seconds in the detail.</summary>
    private async Task<Result> AcquireAsync(
        AutomationPrincipal principal,
        string operation,
        int permitLimit,
        CancellationToken ct
    )
    {
        RateLimitLease lease = await _rateLimiter.AcquireAsync(
            $"automation:{principal.TokenId}:{operation}",
            permitLimit,
            Window,
            ct
        );
        if (lease.IsAcquired)
            return Result.Success();
        int retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(lease.RetryAfter.TotalSeconds));
        return Result.Failure(
            $"Rate limit exceeded for '{operation}' — retry in {retryAfterSeconds}s.",
            "RATE_LIMITED",
            retryAfterSeconds.ToString()
        );
    }

    private static Result<T> Forbidden<T>(string scope) =>
        Result.Failure<T>($"This token lacks the '{scope}' scope.", "FORBIDDEN");
}
