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
using NomNomzBot.Api.Identifiers;

namespace NomNomzBot.Api.Tests.Identifiers;

/// <summary>
/// The response/request JSON path (System.Text.Json — the serializer MVC's <c>AddJsonOptions</c> configures). A
/// <see cref="Guid"/> field serializes to a ULID string; deserialization accepts the ULID it emitted AND a raw
/// Guid string; <c>Guid?</c> null stays null; a malformed id is a hard <see cref="JsonException"/>, not a silent
/// default.
/// </summary>
public sealed class UlidGuidJsonConverterTests
{
    private static readonly Guid KnownId = Guid.Parse("0192a000-0000-7000-8000-000000000c01");

    // Mirrors Program.cs's AddJsonOptions: camelCase + ignore-null + the ULID Guid converter.
    private static JsonSerializerOptions Options()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new UlidGuidJsonConverter());
        return options;
    }

    private sealed record IdCarrier(Guid Id, Guid? OptionalId, string Name);

    [Fact]
    public void Serializes_a_guid_field_as_a_ulid_string()
    {
        string json = JsonSerializer.Serialize(new IdCarrier(KnownId, null, "timer"), Options());

        using JsonDocument doc = JsonDocument.Parse(json);
        string? id = doc.RootElement.GetProperty("id").GetString();

        id.Should().Be(GuidUlidCodec.Encode(KnownId));
        id.Should().HaveLength(26);
        id.Should().NotBe(KnownId.ToString());
    }

    [Fact]
    public void Round_trips_the_emitted_ulid_back_to_the_same_guid()
    {
        JsonSerializerOptions options = Options();
        string json = JsonSerializer.Serialize(new IdCarrier(KnownId, KnownId, "x"), options);

        IdCarrier? back = JsonSerializer.Deserialize<IdCarrier>(json, options);

        back.Should().NotBeNull();
        back!.Id.Should().Be(KnownId);
        back.OptionalId.Should().Be(KnownId);
    }

    [Fact]
    public void Deserializes_a_raw_guid_string_too_for_inbound_tolerance()
    {
        string json = $$"""{"id":"{{KnownId}}","name":"x"}""";

        IdCarrier? back = JsonSerializer.Deserialize<IdCarrier>(json, Options());

        back.Should().NotBeNull();
        back!.Id.Should().Be(KnownId);
        back.OptionalId.Should().BeNull();
    }

    [Fact]
    public void Nullable_guid_null_is_omitted_and_round_trips_to_null()
    {
        JsonSerializerOptions options = Options();
        string json = JsonSerializer.Serialize(new IdCarrier(KnownId, null, "x"), options);

        json.Should().NotContain("optionalId");
        IdCarrier? back = JsonSerializer.Deserialize<IdCarrier>(json, options);
        back!.OptionalId.Should().BeNull();
    }

    [Fact]
    public void Malformed_id_throws_json_exception()
    {
        string json = """{"id":"not-a-valid-id","name":"x"}""";

        Action act = () => JsonSerializer.Deserialize<IdCarrier>(json, Options());

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Non_string_id_token_throws_json_exception()
    {
        string json = """{"id":12345,"name":"x"}""";

        Action act = () => JsonSerializer.Deserialize<IdCarrier>(json, Options());

        act.Should().Throw<JsonException>();
    }
}
