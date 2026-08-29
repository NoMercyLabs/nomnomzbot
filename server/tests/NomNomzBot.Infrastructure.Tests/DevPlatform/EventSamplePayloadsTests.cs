// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json.Nodes;
using FluentAssertions;
using NomNomzBot.Application.DevPlatform;
using NomNomzBot.Application.DevPlatform.Dtos;
using NomNomzBot.Infrastructure.DevPlatform;

namespace NomNomzBot.Infrastructure.Tests.DevPlatform;

/// <summary>
/// Proves the SDK event catalog's <see cref="EventCatalogItemDto.SamplePayloadJson"/> is the SAME real fixture
/// the corresponding translator test proves against — not an approximation authored from memory. For each of the
/// five verified events, this parses both the catalog's sample and the translator test's own raw-string fixture
/// as JSON and asserts they carry the identical top-level key set, which only holds if the catalog value was
/// literally copied from that fixture. Every other handleable event must carry no sample at all, so a future
/// fabricated payload cannot slip in unnoticed.
/// </summary>
public sealed class EventSamplePayloadsTests
{
    private static SdkTypeEmitter RealEmitter() => new(new EventCatalog());

    private static ISet<string> TopLevelKeys(string json) =>
        ((JsonObject)JsonNode.Parse(json)!).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);

    [Theory]
    // wire name in the SDK catalog -> the exact fixture literal from the translator's own behaviour test.
    [InlineData(
        "community.follow",
        """
            {
                "user_id": "1234",
                "user_login": "cool_user",
                "user_name": "Cool_User",
                "broadcaster_user_id": "broadcaster-99",
                "broadcaster_user_login": "streamer",
                "broadcaster_user_name": "Streamer",
                "followed_at": "2026-06-20T11:29:00Z"
            }
            """
    )]
    [InlineData(
        "rewards.new.subscription",
        """
            {
                "user_id": "1234",
                "user_login": "cool_user",
                "user_name": "Cool_User",
                "broadcaster_user_id": "broadcaster-99",
                "tier": "1000",
                "is_gift": false
            }
            """
    )]
    [InlineData(
        "rewards.reward.redeemed",
        """
            {
                "id": "17fa2df1-ad76-4804-bfa5-a40ef63efe63",
                "broadcaster_user_id": "1337",
                "user_id": "9001",
                "user_login": "cooler_user",
                "user_name": "Cooler_User",
                "user_input": "pogchamp",
                "status": "unfulfilled",
                "reward": {
                    "id": "92af127c-7326-4483-a52b-b0da0be61c01",
                    "title": "title",
                    "cost": 100,
                    "prompt": "reward prompt"
                },
                "redeemed_at": "2020-07-15T17:16:03.17106713Z"
            }
            """
    )]
    [InlineData(
        "community.poll.began",
        """
            {
                "id": "poll-1",
                "broadcaster_user_id": "1337",
                "title": "Pineapple on pizza?",
                "choices": [
                    { "id": "c1", "title": "Yes", "bits_votes": 0, "channel_points_votes": 10, "votes": 10 },
                    { "id": "c2", "title": "No", "bits_votes": 0, "channel_points_votes": 0, "votes": 0 }
                ],
                "started_at": "2026-06-20T11:30:00Z",
                "ends_at": "2026-06-20T11:32:00Z"
            }
            """
    )]
    [InlineData(
        "community.prediction.began",
        """
            {
                "id": "pred-1",
                "title": "Will we win?",
                "outcomes": [
                    { "id": "o1", "title": "Yes", "color": "blue", "users": 0, "channel_points": 0 },
                    { "id": "o2", "title": "No", "color": "pink", "users": 0, "channel_points": 0 }
                ],
                "started_at": "2026-06-20T11:30:00Z",
                "locks_at": "2026-06-20T11:31:30Z"
            }
            """
    )]
    [InlineData(
        "stream.raid",
        """
            {
                "from_broadcaster_user_id": "5678",
                "from_broadcaster_user_login": "raiding_streamer",
                "from_broadcaster_user_name": "Raiding_Streamer",
                "to_broadcaster_user_id": "broadcaster-99",
                "to_broadcaster_user_login": "streamer",
                "to_broadcaster_user_name": "Streamer",
                "viewers": 250
            }
            """
    )]
    public void Catalog_sample_payload_matches_the_translator_tests_own_fixture_shape(
        string wireName,
        string translatorTestFixtureJson
    )
    {
        EventCatalogItemDto item = RealEmitter()
            .EmitEventCatalog(SdkContext.Script)
            .Single(c => c.WireName == wireName);

        item.SamplePayloadJson.Should().NotBeNull($"'{wireName}' has a verified real fixture");
        TopLevelKeys(item.SamplePayloadJson!)
            .Should()
            .BeEquivalentTo(
                TopLevelKeys(translatorTestFixtureJson),
                "the catalog sample must be the same real fixture the translator test proves against"
            );
    }

    [Fact]
    public void Events_with_no_verified_fixture_carry_no_fabricated_sample()
    {
        IReadOnlyList<EventCatalogItemDto> catalog = RealEmitter()
            .EmitEventCatalog(SdkContext.Script);

        // chat.message has no translator-test fixture pinned in EventSamplePayloads — it must stay null,
        // never a made-up payload standing in for a real one.
        catalog.Single(c => c.WireName == "chat.message").SamplePayloadJson.Should().BeNull();
    }
}
