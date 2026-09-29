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
using FluentAssertions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Platform.Transport.Helix.SubClients;
using NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix.SubClients.Fakes;

namespace NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix.SubClients;

/// <summary>
/// The conduit sub-client builds the exact Helix request for every conduit endpoint on the APP token (Twitch
/// refuses conduits on a user token), and reads both halves of the Update Conduit Shards answer — a refused
/// shard arrives in <c>errors</c> on a 202, not as a failed call, and dropping it would report a claim that
/// never happened.
/// </summary>
public class TwitchEventSubConduitsApiTests
{
    private static readonly JsonSerializerOptions Snake = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public async Task Every_conduit_call_rides_the_app_token()
    {
        CapturingHelixTransport transport = new()
        {
            ListResult = new List<TwitchConduit>(),
            SingleResult = new TwitchConduit("c-1", 2),
            PageResult = new TwitchPage<TwitchConduitShard>([], null, 0),
            RawResult = """{"data":[],"errors":[]}""",
        };
        TwitchEventSubConduitsApi api = new(transport);

        await api.GetConduitsAsync();
        await api.CreateConduitAsync(2);
        await api.UpdateConduitAsync("c-1", 2);
        await api.DeleteConduitAsync("c-1");
        await api.GetConduitShardsAsync("c-1");
        await api.UpdateConduitShardsAsync(
            "c-1",
            [TwitchConduitShardAssignment.ForWebSocket("0", "s-1")]
        );

        transport.Requests.Should().HaveCount(6);
        transport.Requests.Should().OnlyContain(r => r.Auth == TwitchHelixAuth.BotApp);
        transport
            .Requests.Select(r => $"{r.Method} {r.Path}")
            .Should()
            .Equal(
                "GET eventsub/conduits",
                "POST eventsub/conduits",
                "PATCH eventsub/conduits",
                "DELETE eventsub/conduits",
                "GET eventsub/conduits/shards",
                "PATCH eventsub/conduits/shards"
            );
    }

    [Fact]
    public async Task Create_and_update_send_the_shard_count_and_id()
    {
        CapturingHelixTransport transport = new() { SingleResult = new TwitchConduit("c-9", 2) };
        TwitchEventSubConduitsApi api = new(transport);

        Result<TwitchConduit> created = await api.CreateConduitAsync(2);
        string createBody = JsonSerializer.Serialize(transport.LastRequest!.Body, Snake);
        await api.UpdateConduitAsync("c-9", 3);
        string updateBody = JsonSerializer.Serialize(transport.LastRequest!.Body, Snake);

        created.Value.Should().Be(new TwitchConduit("c-9", 2));
        createBody.Should().Be("""{"shard_count":2}""");
        updateBody.Should().Be("""{"id":"c-9","shard_count":3}""");
    }

    [Fact]
    public async Task Delete_targets_the_conduit_id_and_treats_already_gone_as_success()
    {
        CapturingHelixTransport transport = new();
        transport.SendResults.Enqueue(Result.Failure("gone", TwitchErrorCodes.NotFound));
        TwitchEventSubConduitsApi api = new(transport);

        Result deleted = await api.DeleteConduitAsync("c-1");

        deleted.IsSuccess.Should().BeTrue();
        transport
            .LastRequest!.Query.Should()
            .ContainSingle()
            .Which.Should()
            .Be(new KeyValuePair<string, string>("id", "c-1"));
    }

    [Fact]
    public async Task Get_shards_filters_by_conduit_and_status_and_maps_the_session()
    {
        CapturingHelixTransport transport = new()
        {
            PageResult = new TwitchPage<TwitchConduitShard>(
                [
                    new("0", "enabled", new("websocket", SessionId: "s-a")),
                    new("1", "websocket_disconnected", new("websocket", SessionId: "s-b")),
                ],
                null,
                0
            ),
        };
        TwitchEventSubConduitsApi api = new(transport);

        Result<IReadOnlyList<TwitchConduitShard>> shards = await api.GetConduitShardsAsync(
            "c-1",
            "enabled"
        );

        transport
            .LastRequest!.Query.Should()
            .Equal(
                new KeyValuePair<string, string>("conduit_id", "c-1"),
                new KeyValuePair<string, string>("status", "enabled")
            );
        shards
            .Value.Select(s => (s.Id, s.IsEnabled, s.Transport!.SessionId))
            .Should()
            .Equal(("0", true, "s-a"), ("1", false, "s-b"));
    }

    [Fact]
    public async Task Update_shards_sends_a_websocket_binding_and_returns_refused_shards()
    {
        CapturingHelixTransport transport = new()
        {
            RawResult = """
                {"data":[{"id":"0","status":"enabled","transport":{"method":"websocket","session_id":"s-new"}}],
                 "errors":[{"id":"1","message":"The shard id is outside the conduit's range","code":"invalid_parameter"}]}
                """,
        };
        TwitchEventSubConduitsApi api = new(transport);

        Result<TwitchConduitShardUpdateResult> result = await api.UpdateConduitShardsAsync(
            "c-1",
            [
                TwitchConduitShardAssignment.ForWebSocket("0", "s-new"),
                TwitchConduitShardAssignment.ForWebSocket("1", "s-new"),
            ]
        );

        string body = JsonSerializer.Serialize(transport.LastRequest!.Body, Snake);
        body.Should()
            .Be(
                """{"conduit_id":"c-1","shards":[{"id":"0","transport":{"method":"websocket","session_id":"s-new"}},{"id":"1","transport":{"method":"websocket","session_id":"s-new"}}]}"""
            );
        result.Value.Shards.Should().ContainSingle(s => s.Id == "0" && s.IsEnabled);
        result
            .Value.Errors.Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new TwitchConduitShardError(
                    "1",
                    "The shard id is outside the conduit's range",
                    "invalid_parameter"
                )
            );
    }
}
