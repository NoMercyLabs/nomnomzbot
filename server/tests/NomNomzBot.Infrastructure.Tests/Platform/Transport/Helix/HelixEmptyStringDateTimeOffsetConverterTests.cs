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
using System.Text.Json.Serialization;
using FluentAssertions;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Platform.Transport.Helix;

namespace NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;

/// <summary>
/// Reproduces the 2026-08-25 live failure: Get Banned Users returns <c>"expires_at": ""</c> for a
/// PERMANENT ban, System.Text.Json could not parse the empty string into <c>DateTimeOffset?</c>, and the
/// entire response failed to deserialize — so every banned-user import on a channel with a permanent ban
/// silently fell back to an empty ban registry.
/// </summary>
public sealed class HelixEmptyStringDateTimeOffsetConverterTests
{
    // Mirrors the real Helix wire options (TwitchHelixTransport.WireJson).
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new HelixEmptyStringDateTimeOffsetConverter() },
    };

    // The exact shape Twitch sends for a permanent ban — empty expires_at, everything else populated.
    private const string PermanentBanJson = """
        {
          "user_id": "1234",
          "user_login": "troll",
          "user_name": "Troll",
          "expires_at": "",
          "created_at": "2026-08-01T12:00:00Z",
          "reason": "spam",
          "moderator_id": "9",
          "moderator_login": "mod",
          "moderator_name": "Mod"
        }
        """;

    [Fact]
    public void A_permanent_bans_empty_expires_at_deserializes_as_null_instead_of_throwing()
    {
        TwitchBannedUser? banned = JsonSerializer.Deserialize<TwitchBannedUser>(
            PermanentBanJson,
            WireJson
        );

        banned.Should().NotBeNull();
        banned!.ExpiresAt.Should().BeNull("an empty expires_at means the ban never expires");
        banned.UserLogin.Should().Be("troll", "the rest of the payload must still be read");
        banned
            .CreatedAt.Should()
            .Be(
                DateTimeOffset.Parse("2026-08-01T12:00:00Z"),
                "a populated timestamp must still parse normally"
            );
    }

    [Fact]
    public void A_timed_bans_real_expires_at_still_parses()
    {
        string timedBan = PermanentBanJson.Replace(
            "\"expires_at\": \"\"",
            "\"expires_at\": \"2026-09-01T08:30:00Z\""
        );

        TwitchBannedUser? banned = JsonSerializer.Deserialize<TwitchBannedUser>(timedBan, WireJson);

        banned!.ExpiresAt.Should().Be(DateTimeOffset.Parse("2026-09-01T08:30:00Z"));
    }

    [Fact]
    public void An_explicit_null_expires_at_is_still_null()
    {
        string nullExpiry = PermanentBanJson.Replace(
            "\"expires_at\": \"\"",
            "\"expires_at\": null"
        );

        TwitchBannedUser? banned = JsonSerializer.Deserialize<TwitchBannedUser>(
            nullExpiry,
            WireJson
        );

        banned!.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public void A_genuinely_malformed_timestamp_is_still_rejected()
    {
        string garbage = PermanentBanJson.Replace(
            "\"expires_at\": \"\"",
            "\"expires_at\": \"not-a-date\""
        );

        Action act = () => JsonSerializer.Deserialize<TwitchBannedUser>(garbage, WireJson);

        act.Should()
            .Throw<JsonException>(
                "tolerating the empty string must not turn every malformed timestamp into a silent null"
            );
    }
}
