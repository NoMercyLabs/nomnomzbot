// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.AutomationApi.Services;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.AutomationApi.Stream;

/// <summary>One live socket, seen protocol-side: text frames in/out plus a server-initiated close.</summary>
public interface IAutomationStreamConnection
{
    /// <summary>Writes one text frame; the implementation serializes concurrent sends.</summary>
    Task SendAsync(string frameJson, CancellationToken ct);

    /// <summary>The next text frame, or null when the client closed the socket.</summary>
    Task<string?> ReceiveTextAsync(CancellationToken ct);

    Task CloseAsync(string reason, CancellationToken ct);
}

/// <summary>
/// The automation stream protocol engine (automation-api.md §4.2) — everything between the WebSocket
/// upgrade and the socket close, over an abstract connection so the protocol is testable without
/// Kestrel. <c>hello</c> goes out on connect; an unauthenticated socket accepts ONLY
/// <c>authenticate</c> and is closed after the advertised timeout; authenticated ops
/// (<c>subscribe</c>/<c>invoke</c>/<c>sendChat</c>) enforce the token's scopes through the same
/// command service the REST plane uses, and every request gets a correlated <c>response</c> frame.
/// Subscribed public events arrive via the session the coordinator registers here.
/// </summary>
public sealed class AutomationStreamCoordinator
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan AuthTimeout = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAutomationSessionRegistry _sessions;
    private readonly IDeploymentProfileService _profile;
    private readonly TimeProvider _clock;
    private readonly ILogger<AutomationStreamCoordinator> _logger;

    public AutomationStreamCoordinator(
        IServiceScopeFactory scopeFactory,
        IAutomationSessionRegistry sessions,
        IDeploymentProfileService profile,
        TimeProvider clock,
        ILogger<AutomationStreamCoordinator> logger
    )
    {
        _scopeFactory = scopeFactory;
        _sessions = sessions;
        _profile = profile;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Runs the socket to completion. <paramref name="headerPrincipal"/> is the native-client handshake auth.</summary>
    public async Task RunAsync(
        IAutomationStreamConnection connection,
        AutomationPrincipal? headerPrincipal,
        CancellationToken ct
    )
    {
        await SendAsync(
            connection,
            new
            {
                op = "hello",
                data = new
                {
                    instanceId = _profile.Current.InstanceId,
                    apiVersion = "1.0",
                    authRequired = headerPrincipal is null,
                    authTimeoutSeconds = (int)AuthTimeout.TotalSeconds,
                },
            },
            ct
        );

        AutomationPrincipal? principal =
            headerPrincipal ?? await AuthenticatePhaseAsync(connection, ct);
        if (principal is null)
            return; // timed out or closed before authenticating

        AutomationSession session = new()
        {
            SessionId = Guid.CreateVersion7().ToString(),
            Principal = principal,
            SendAsync = connection.SendAsync,
        };
        _sessions.Register(session);
        try
        {
            string? frame;
            while ((frame = await connection.ReceiveTextAsync(ct)) is not null)
                await HandleFrameAsync(connection, session, frame, ct);
        }
        finally
        {
            _sessions.Unregister(session.SessionId);
        }
    }

    /// <summary>Browser-client auth window: only <c>authenticate</c> is accepted; the socket closes on timeout.</summary>
    private async Task<AutomationPrincipal?> AuthenticatePhaseAsync(
        IAutomationStreamConnection connection,
        CancellationToken ct
    )
    {
        using CancellationTokenSource timeout = new(AuthTimeout, _clock);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            ct,
            timeout.Token
        );
        try
        {
            string? frame;
            while ((frame = await connection.ReceiveTextAsync(linked.Token)) is not null)
            {
                using JsonDocument doc = JsonDocument.Parse(frame);
                string op = GetString(doc, "op") ?? "";
                string? id = GetString(doc, "id");
                if (op != "authenticate")
                {
                    await RespondErrorAsync(
                        connection,
                        id,
                        "unauthenticated",
                        "Authenticate first.",
                        ct
                    );
                    continue;
                }

                string secret = GetString(doc, "token") ?? "";
                using IServiceScope scope = _scopeFactory.CreateScope();
                IAutomationTokenAuthenticator authenticator =
                    scope.ServiceProvider.GetRequiredService<IAutomationTokenAuthenticator>();
                Result<AutomationPrincipal> authenticated = await authenticator.AuthenticateAsync(
                    secret,
                    ct
                );
                if (authenticated.IsFailure)
                {
                    await RespondErrorAsync(
                        connection,
                        id,
                        "unauthenticated",
                        "Invalid automation token.",
                        ct
                    );
                    continue; // the client may retry until the timeout closes the window
                }

                await RespondOkAsync(connection, id, new { authenticated = true }, ct);
                return authenticated.Value;
            }
            return null; // client closed
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            await connection.CloseAsync("authentication timeout", CancellationToken.None);
            return null;
        }
    }

    private async Task HandleFrameAsync(
        IAutomationStreamConnection connection,
        AutomationSession session,
        string frame,
        CancellationToken ct
    )
    {
        string? id = null;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(frame);
            string op = GetString(doc, "op") ?? "";
            id = GetString(doc, "id");
            switch (op)
            {
                case "subscribe":
                    await HandleSubscribeAsync(connection, session, doc, id, ct);
                    break;
                case "invoke":
                    await HandleInvokeAsync(connection, session, doc, id, ct);
                    break;
                case "sendChat":
                    await HandleSendChatAsync(connection, session, doc, id, ct);
                    break;
                case "authenticate":
                    await RespondErrorAsync(
                        connection,
                        id,
                        "already_authenticated",
                        "This socket is already authenticated.",
                        ct
                    );
                    break;
                default:
                    await RespondErrorAsync(
                        connection,
                        id,
                        "unknown_op",
                        $"Unknown op '{op}'.",
                        ct
                    );
                    break;
            }
        }
        catch (JsonException)
        {
            await RespondErrorAsync(
                connection,
                id,
                "invalid_request",
                "Frames must be one JSON object.",
                ct
            );
        }
    }

    private async Task HandleSubscribeAsync(
        IAutomationStreamConnection connection,
        AutomationSession session,
        JsonDocument doc,
        string? id,
        CancellationToken ct
    )
    {
        if (!session.Principal.Scopes.Contains("events"))
        {
            await RespondErrorAsync(
                connection,
                id,
                "scope_denied",
                "This token lacks the 'events' scope.",
                ct
            );
            return;
        }

        List<string> patterns = [];
        if (
            doc.RootElement.TryGetProperty("events", out JsonElement eventsEl)
            && eventsEl.ValueKind == JsonValueKind.Array
        )
        {
            foreach (JsonElement item in eventsEl.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String)
                    patterns.Add(item.GetString()!);
        }
        if (patterns.Count == 0)
        {
            await RespondErrorAsync(
                connection,
                id,
                "invalid_request",
                "subscribe needs a non-empty 'events' array.",
                ct
            );
            return;
        }

        session.Subscribe(patterns);
        await RespondOkAsync(connection, id, new { subscribed = patterns }, ct);
    }

    private async Task HandleInvokeAsync(
        IAutomationStreamConnection connection,
        AutomationSession session,
        JsonDocument doc,
        string? id,
        CancellationToken ct
    )
    {
        Dictionary<string, string>? variables = null;
        if (
            doc.RootElement.TryGetProperty("variables", out JsonElement varsEl)
            && varsEl.ValueKind == JsonValueKind.Object
        )
        {
            variables = new Dictionary<string, string>();
            foreach (JsonProperty property in varsEl.EnumerateObject())
                variables[property.Name] = property.Value.ToString();
        }

        List<string>? args = null;
        if (
            doc.RootElement.TryGetProperty("args", out JsonElement argsEl)
            && argsEl.ValueKind == JsonValueKind.Array
        )
            args = [.. argsEl.EnumerateArray().Select(a => a.ToString())];

        AutomationInvokeRequest request = new()
        {
            PipelineId = Guid.TryParse(GetString(doc, "pipelineId"), out Guid pipelineId)
                ? pipelineId
                : null,
            PipelineName = GetString(doc, "pipelineName"),
            Args = args,
            Variables = variables,
        };

        using IServiceScope scope = _scopeFactory.CreateScope();
        IAutomationCommandService commands =
            scope.ServiceProvider.GetRequiredService<IAutomationCommandService>();
        Result<AutomationInvokeResult> result = await commands.InvokePipelineAsync(
            session.Principal,
            request,
            ct
        );
        if (result.IsFailure)
        {
            await RespondFailureAsync(connection, id, result.ErrorCode, result.ErrorMessage, ct);
            return;
        }
        await RespondOkAsync(
            connection,
            id,
            new
            {
                pipelineId = result.Value.PipelineId,
                executionId = result.Value.ExecutionId,
                accepted = result.Value.Accepted,
            },
            ct
        );
    }

    private async Task HandleSendChatAsync(
        IAutomationStreamConnection connection,
        AutomationSession session,
        JsonDocument doc,
        string? id,
        CancellationToken ct
    )
    {
        AutomationChatRequest request = new()
        {
            Text = GetString(doc, "text") ?? "",
            ReplyToMessageId = GetString(doc, "replyToMessageId"),
            WhisperToTwitchUserId = GetString(doc, "whisperToTwitchUserId"),
        };

        using IServiceScope scope = _scopeFactory.CreateScope();
        IAutomationCommandService commands =
            scope.ServiceProvider.GetRequiredService<IAutomationCommandService>();
        Result result = await commands.SendChatAsync(session.Principal, request, ct);
        if (result.IsFailure)
        {
            await RespondFailureAsync(connection, id, result.ErrorCode, result.ErrorMessage, ct);
            return;
        }
        await RespondOkAsync(connection, id, new { sent = true }, ct);
    }

    /// <summary>Maps the Result error vocabulary onto the §4.2 wire codes.</summary>
    private static async Task RespondFailureAsync(
        IAutomationStreamConnection connection,
        string? id,
        string? errorCode,
        string? message,
        CancellationToken ct
    )
    {
        string wireCode = errorCode switch
        {
            "FORBIDDEN" => "scope_denied",
            "RATE_LIMITED" => "rate_limited",
            "NOT_FOUND" => "not_found",
            "VALIDATION_FAILED" => "invalid_request",
            "UNAUTHENTICATED" => "unauthenticated",
            _ => "internal_error",
        };
        await RespondErrorAsync(connection, id, wireCode, message ?? "Request failed.", ct);
    }

    private static Task RespondOkAsync(
        IAutomationStreamConnection connection,
        string? id,
        object data,
        CancellationToken ct
    ) =>
        SendAsync(
            connection,
            new
            {
                op = "response",
                id,
                status = "ok",
                data,
            },
            ct
        );

    private static Task RespondErrorAsync(
        IAutomationStreamConnection connection,
        string? id,
        string code,
        string message,
        CancellationToken ct
    ) =>
        SendAsync(
            connection,
            new
            {
                op = "response",
                id,
                status = "error",
                error = new { code, message },
            },
            ct
        );

    private static Task SendAsync(
        IAutomationStreamConnection connection,
        object frame,
        CancellationToken ct
    ) => connection.SendAsync(JsonSerializer.Serialize(frame, WireJson), ct);

    private static string? GetString(JsonDocument doc, string property) =>
        doc.RootElement.TryGetProperty(property, out JsonElement el)
        && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;
}
