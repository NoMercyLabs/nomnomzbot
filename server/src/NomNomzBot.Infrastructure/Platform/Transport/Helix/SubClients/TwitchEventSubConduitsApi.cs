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
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Transport.Helix.SubClients;

/// <summary>
/// The Helix "EventSub conduits" sub-client (twitch-eventsub.md §10). Pure Helix I/O on the app access
/// token: conduits belong to the app, not to a tenant, so no tenant resolution or scope pre-check applies.
/// </summary>
public sealed class TwitchEventSubConduitsApi(ITwitchHelixTransport transport)
    : ITwitchEventSubConduitsApi
{
    private const string ConduitsPath = "eventsub/conduits";
    private const string ShardsPath = "eventsub/conduits/shards";

    // Update Conduit Shards answers { data: [...], errors: [...] }; the shared envelope reader keeps only
    // data[0], so this one endpoint reads the raw body and parses both arrays itself.
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public Task<Result<IReadOnlyList<TwitchConduit>>> GetConduitsAsync(
        CancellationToken ct = default
    ) =>
        transport.GetListAsync<TwitchConduit>(
            new(HttpMethod.Get, ConduitsPath, TwitchHelixAuth.BotApp),
            ct
        );

    public Task<Result<TwitchConduit>> CreateConduitAsync(
        int shardCount,
        CancellationToken ct = default
    ) =>
        transport.SendWithResultAsync<TwitchConduit>(
            new(
                HttpMethod.Post,
                ConduitsPath,
                TwitchHelixAuth.BotApp,
                Body: new { shard_count = shardCount }
            ),
            ct
        );

    public Task<Result<TwitchConduit>> UpdateConduitAsync(
        string conduitId,
        int shardCount,
        CancellationToken ct = default
    ) =>
        transport.SendWithResultAsync<TwitchConduit>(
            new(
                HttpMethod.Patch,
                ConduitsPath,
                TwitchHelixAuth.BotApp,
                Body: new { id = conduitId, shard_count = shardCount }
            ),
            ct
        );

    public async Task<Result> DeleteConduitAsync(string conduitId, CancellationToken ct = default)
    {
        Result deleted = await transport.SendAsync(
            new(
                HttpMethod.Delete,
                ConduitsPath,
                TwitchHelixAuth.BotApp,
                Query: [new("id", conduitId)]
            ),
            ct
        );
        return deleted is { IsFailure: true, ErrorCode: TwitchErrorCodes.NotFound }
            ? Result.Success()
            : deleted;
    }

    public async Task<Result<IReadOnlyList<TwitchConduitShard>>> GetConduitShardsAsync(
        string conduitId,
        string? status = null,
        CancellationToken ct = default
    )
    {
        List<TwitchConduitShard> shards = [];
        string? cursor = null;
        do
        {
            List<KeyValuePair<string, string>> query = [new("conduit_id", conduitId)];
            if (status is not null)
                query.Add(new("status", status));
            if (cursor is not null)
                query.Add(new("after", cursor));

            Result<TwitchPage<TwitchConduitShard>> page =
                await transport.GetPageAsync<TwitchConduitShard>(
                    new(HttpMethod.Get, ShardsPath, TwitchHelixAuth.BotApp, Query: query),
                    ct
                );
            if (page.IsFailure)
                return page.WithValue<IReadOnlyList<TwitchConduitShard>>(default!);

            shards.AddRange(page.Value.Items);
            cursor = page.Value.NextCursor;
        } while (!string.IsNullOrEmpty(cursor));

        return Result.Success<IReadOnlyList<TwitchConduitShard>>(shards);
    }

    public async Task<Result<TwitchConduitShardUpdateResult>> UpdateConduitShardsAsync(
        string conduitId,
        IReadOnlyList<TwitchConduitShardAssignment> shards,
        CancellationToken ct = default
    )
    {
        object body = new
        {
            conduit_id = conduitId,
            shards = shards
                .Select(s => new
                {
                    id = s.Id,
                    transport = new
                    {
                        method = s.Method,
                        session_id = s.SessionId,
                        callback = s.Callback,
                        secret = s.Secret,
                    },
                })
                .ToList(),
        };

        Result<string> raw = await transport.GetRawAsync(
            new(HttpMethod.Patch, ShardsPath, TwitchHelixAuth.BotApp, Body: body),
            ct
        );
        if (raw.IsFailure)
            return raw.WithValue<TwitchConduitShardUpdateResult>(default!);

        ShardUpdateEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ShardUpdateEnvelope>(raw.Value, WireJson);
        }
        catch (JsonException ex)
        {
            return Result.Failure<TwitchConduitShardUpdateResult>(
                "Twitch answered Update Conduit Shards with an unreadable body.",
                TwitchErrorCodes.Transport,
                ex.Message
            );
        }

        return Result.Success(
            new TwitchConduitShardUpdateResult(envelope?.Data ?? [], envelope?.Errors ?? [])
        );
    }

    private sealed record ShardUpdateEnvelope(
        List<TwitchConduitShard>? Data,
        List<TwitchConduitShardError>? Errors
    );
}
