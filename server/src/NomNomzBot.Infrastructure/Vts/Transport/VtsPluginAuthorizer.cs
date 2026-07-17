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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Vts.Services;
using NomNomzBot.Domain.Vts.Entities;
using NomNomzBot.Infrastructure.Obs.Transport;

namespace NomNomzBot.Infrastructure.Vts.Transport;

/// <summary>
/// The one-time plugin approval (vtube-studio.md §0 D2): dial the channel's VTS endpoint, send
/// <c>AuthenticationTokenRequest</c>, and WAIT — VTube Studio answers only after the streamer clicks
/// Allow (or Deny) on the in-app popup. A granted token is sealed onto the connection row; a denial
/// or timeout stores nothing. Runs on a short-lived socket of its own, not the control session.
/// </summary>
public sealed class VtsPluginAuthorizer : IVtsPluginAuthorizer
{
    /// <summary>The streamer has to reach for the mouse — give them a real window.</summary>
    private static readonly TimeSpan ApprovalTimeout = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    private readonly IObsSocketFactory _socketFactory;
    private readonly IApplicationDbContext _db;
    private readonly IVtsConnectionService _connections;
    private readonly TimeProvider _clock;
    private readonly ILogger<VtsPluginAuthorizer> _logger;

    public VtsPluginAuthorizer(
        IObsSocketFactory socketFactory,
        IApplicationDbContext db,
        IVtsConnectionService connections,
        TimeProvider clock,
        ILogger<VtsPluginAuthorizer> logger
    )
    {
        _socketFactory = socketFactory;
        _db = db;
        _connections = connections;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result> AuthorizeAsync(Guid broadcasterId, CancellationToken ct = default)
    {
        VtsConnection? config = await _db.VtsConnections.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId,
            ct
        );
        if (config is null || !config.IsEnabled)
            return Result.Failure(
                "VTube Studio control is not enabled for this channel.",
                "VTS_DISABLED"
            );
        if (config.Mode != "direct")
            return Result.Failure(
                "The authorize flow runs on the direct connection; bridge channels authorize through the bridge.",
                "VTS_WRONG_MODE"
            );

        IObsSocket socket;
        try
        {
            socket = await _socketFactory.ConnectAsync(new Uri(config.Endpoint), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "VTS authorize connect failed for channel {Channel}.",
                broadcasterId
            );
            return Result.Failure(
                "Could not connect to VTube Studio at the configured endpoint.",
                "VTS_NOT_CONNECTED"
            );
        }

        await using (socket)
        {
            string requestId = Guid.CreateVersion7().ToString();
            string frame = DirectVtsTransport.BuildFrame(
                "AuthenticationTokenRequest",
                requestId,
                JsonSerializer.Serialize(
                    new
                    {
                        pluginName = DirectVtsTransport.PluginName,
                        pluginDeveloper = DirectVtsTransport.PluginDeveloper,
                    },
                    WireJson
                )
            );
            await socket.SendAsync(frame, ct);

            using CancellationTokenSource timeout = new(ApprovalTimeout, _clock);
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
                ct,
                timeout.Token
            );
            try
            {
                string? reply;
                while ((reply = await socket.ReceiveTextAsync(linked.Token)) is not null)
                {
                    using JsonDocument doc = JsonDocument.Parse(reply);
                    string? replyId = doc.RootElement.TryGetProperty(
                        "requestID",
                        out JsonElement idEl
                    )
                        ? idEl.GetString()
                        : null;
                    if (replyId != requestId)
                        continue; // unrelated frame (an event) — keep waiting

                    string messageType =
                        doc.RootElement.GetProperty("messageType").GetString() ?? "";
                    if (messageType == "APIError")
                    {
                        string message = doc
                            .RootElement.GetProperty("data")
                            .TryGetProperty("message", out JsonElement messageEl)
                            ? messageEl.GetString() ?? "VTube Studio denied the plugin."
                            : "VTube Studio denied the plugin.";
                        return Result.Failure(message, "VTS_DENIED");
                    }

                    string? token =
                        doc.RootElement.TryGetProperty("data", out JsonElement dataEl)
                        && dataEl.TryGetProperty("authenticationToken", out JsonElement tokenEl)
                            ? tokenEl.GetString()
                            : null;
                    if (string.IsNullOrEmpty(token))
                        return Result.Failure(
                            "VTube Studio answered without a token — the streamer likely denied the popup.",
                            "VTS_DENIED"
                        );

                    return await _connections.StorePluginTokenAsync(broadcasterId, token, ct);
                }
                return Result.Failure(
                    "VTube Studio closed the connection before answering.",
                    "VTS_NOT_CONNECTED"
                );
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                return Result.Failure(
                    "The streamer did not approve the plugin within the time window.",
                    "VTS_TIMEOUT"
                );
            }
        }
    }
}
