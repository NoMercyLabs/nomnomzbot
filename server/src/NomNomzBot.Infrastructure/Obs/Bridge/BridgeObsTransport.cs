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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;

namespace NomNomzBot.Infrastructure.Obs.Bridge;

/// <summary>
/// The SaaS/remote OBS transport (obs-control.md §3.2/D2): routes each command to the channel's
/// LEADER bridge over the relay hub and awaits its ack. No leader online → <c>OBS_BRIDGE_OFFLINE</c>
/// (graceful, never silent). Batches ride the same push as one payload the bridge fans out locally.
/// </summary>
public sealed class BridgeObsTransport : IObsTransport
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    private readonly IObsBridgeRegistry _registry;
    private readonly IObsBridgePusher _pusher;
    private readonly ObsBridgeCommandBook _commands;
    private readonly TimeProvider _clock;

    public BridgeObsTransport(
        IObsBridgeRegistry registry,
        IObsBridgePusher pusher,
        ObsBridgeCommandBook commands,
        TimeProvider clock
    )
    {
        _registry = registry;
        _pusher = pusher;
        _commands = commands;
        _clock = clock;
    }

    public async Task<Result<ObsResponse>> SendAsync(
        Guid broadcasterId,
        Guid commandId,
        ObsRequest request,
        CancellationToken ct = default
    )
    {
        string payload = JsonSerializer.Serialize(
            new
            {
                kind = "request",
                requestType = request.RequestType,
                requestData = request.RequestData,
            },
            WireJson
        );
        return await RoundTripAsync(broadcasterId, commandId, payload, ct);
    }

    public async Task<Result<IReadOnlyList<ObsResponse>>> SendBatchAsync(
        Guid broadcasterId,
        Guid commandId,
        ObsRequestBatch batch,
        CancellationToken ct = default
    )
    {
        string payload = JsonSerializer.Serialize(
            new
            {
                kind = "batch",
                executionType = (int)batch.Execution,
                haltOnFailure = batch.HaltOnFailure,
                requests = batch
                    .Requests.Select(r => new
                    {
                        requestType = r.RequestType,
                        requestData = r.RequestData,
                    })
                    .ToArray(),
            },
            WireJson
        );
        Result<ObsResponse> result = await RoundTripAsync(broadcasterId, commandId, payload, ct);
        if (result.IsFailure)
            return Result.Failure<IReadOnlyList<ObsResponse>>(
                result.ErrorMessage!,
                result.ErrorCode!
            );
        // The bridge folds a batch into one ack; per-item results ride the response data as JSON.
        return Result.Success<IReadOnlyList<ObsResponse>>([result.Value]);
    }

    private async Task<Result<ObsResponse>> RoundTripAsync(
        Guid broadcasterId,
        Guid commandId,
        string payload,
        CancellationToken ct
    )
    {
        string? leader = await _registry.GetLeaderAsync(broadcasterId, ct);
        if (leader is null)
            return Result.Failure<ObsResponse>(
                "No OBS bridge is connected for this channel.",
                "OBS_BRIDGE_OFFLINE"
            );

        Task<ObsResponse> ack = _commands.BeginAsync(commandId);
        try
        {
            await _pusher.PushExecuteAsync(leader, commandId, payload, ct);
            ObsResponse response = await ack.WaitAsync(CommandTimeout, _clock, ct);
            return Result.Success(response);
        }
        catch (TimeoutException)
        {
            _commands.Abandon(commandId);
            return Result.Failure<ObsResponse>(
                "The OBS bridge did not answer within the timeout.",
                "OBS_TIMEOUT"
            );
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Failure<ObsResponse>(
                "The OBS bridge disconnected mid-command.",
                "OBS_BRIDGE_OFFLINE"
            );
        }
    }
}
