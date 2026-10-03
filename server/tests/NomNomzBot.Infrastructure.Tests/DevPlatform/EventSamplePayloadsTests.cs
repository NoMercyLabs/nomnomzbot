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
using NomNomzBot.Application.DevPlatform.Services;
using NomNomzBot.Domain.Billing.Events;
using NomNomzBot.Domain.Discord.Events;
using NomNomzBot.Domain.Economy.Events;
using NomNomzBot.Domain.Giveaways.Events;
using NomNomzBot.Domain.Tts.Events;
using NomNomzBot.Infrastructure.DevPlatform;

namespace NomNomzBot.Infrastructure.Tests.DevPlatform;

/// <summary>
/// Proves the SDK event catalog's <see cref="EventCatalogItemDto.SamplePayloadJson"/> is the SAME real fixture
/// the corresponding translator test proves against — not an approximation authored from memory. For every
/// verified event, this parses both the catalog's sample and the translator test's own raw-string fixture as
/// JSON and asserts they carry the identical top-level key set, which only holds if the catalog value was
/// literally copied from that fixture. Every other handleable event must carry no sample at all, so a future
/// fabricated payload cannot slip in unnoticed.
/// </summary>
public sealed class EventSamplePayloadsTests
{
    private static SdkTypeEmitter RealEmitter() => new(new EventCatalog());

    // No wired event's sample currently carries a non-integer double value, so the "number"-scalar branch of
    // AssertScalarMatchesType had never been exercised (a mutation to it did not go red). This pins it directly.
    [Fact]
    public void AssertSampleConformsToSchema_accepts_a_double_value_against_a_number_typed_property()
    {
        JsonNode schema = JsonNode.Parse(
            """{ "properties": { "score": { "type": "number" } } }"""
        )!;
        string sample = """{ "score": 3.14 }""";

        Action act = () => AssertSampleConformsToSchema(sample, schema);

        act.Should().NotThrow();
    }

    [Fact]
    public void AssertSampleConformsToSchema_rejects_a_double_value_against_a_boolean_typed_property()
    {
        JsonNode schema = JsonNode.Parse(
            """{ "properties": { "score": { "type": "boolean" } } }"""
        )!;
        string sample = """{ "score": 3.14 }""";

        Action act = () => AssertSampleConformsToSchema(sample, schema);

        act.Should()
            .Throw<Exception>("a double value must not satisfy a boolean-typed schema property");
    }

    [Fact]
    public void Internal_event_with_no_translator_fixture_gets_a_reflection_generated_sample()
    {
        IReadOnlyList<EventCatalogItemDto> catalog = RealEmitter()
            .EmitEventCatalog(SdkContext.Script);

        // commands.command.executed has no translator-test fixture pinned in EventSamplePayloads (there is no
        // EventSub topic for it — it is raised by the pipeline engine itself), so it must fall back to a
        // reflection-generated sample rather than staying null — every catalog event now carries SOME real
        // (fixture-sourced or reflection-generated) sample.
        EventCatalogItemDto item = catalog.Single(c => c.WireName == "commands.command.executed");
        item.SamplePayloadJson.Should().NotBeNull();
        AssertSampleConformsToSchema(item.SamplePayloadJson!, item.PayloadSchema);
    }

    [Theory]
    [InlineData(typeof(SubscriptionTierChangedEvent))] // billing
    [InlineData(typeof(TtsUtteranceDispatchedEvent))] // tts
    [InlineData(typeof(GiveawayOpenedEvent))] // giveaways
    [InlineData(typeof(DiscordNotificationDispatchedEvent))] // discord
    [InlineData(typeof(CurrencyCreditedEvent))] // economy
    public void Reflection_generated_sample_for_internal_event_conforms_to_its_own_json_schema(
        Type clrType
    )
    {
        EventCatalog catalog = new();
        EventDescriptor descriptor = catalog.Descriptors.Single(d => d.ClrType == clrType);

        EventCatalogItemDto item = RealEmitter()
            .EmitEventCatalog(SdkContext.Script)
            .Single(c => c.WireName == descriptor.WireName);

        item.SamplePayloadJson.Should().NotBeNull();
        AssertSampleConformsToSchema(item.SamplePayloadJson!, item.PayloadSchema);
    }

    private static JsonObject SampleOf(string wireName) =>
        (JsonObject)
            JsonNode.Parse(
                RealEmitter()
                    .EmitEventCatalog(SdkContext.Script)
                    .Single(c => c.WireName == wireName)
                    .SamplePayloadJson!
            )!;

    [Fact]
    public void Chat_message_sample_is_the_translated_event_with_the_fixtures_real_values()
    {
        JsonObject sample = SampleOf("chat.message");

        sample["messageId"]!.GetValue<string>().Should().Be("abc-123");
        sample["userId"]!.GetValue<string>().Should().Be("555");
        sample["userLogin"]!.GetValue<string>().Should().Be("cool_user");
        sample["userDisplayName"]!.GetValue<string>().Should().Be("Cool_User");
        sample["message"]!.GetValue<string>().Should().Be("hello world Kappa");
        sample["colorHex"]!.GetValue<string>().Should().Be("#FF0000");
        sample["twitchBroadcasterId"]!.GetValue<string>().Should().Be("broadcaster-99");
        sample["fragments"]!.AsArray().Should().HaveCount(2);
        sample.ContainsKey("message_id").Should().BeFalse("a script never receives the wire names");
        sample.ContainsKey("chatter_user_login").Should().BeFalse();
    }

    [Fact]
    public void Follow_sample_is_the_translated_event_with_the_fixtures_real_values()
    {
        JsonObject sample = SampleOf("community.follow");

        sample["userId"]!.GetValue<string>().Should().Be("1234");
        sample["userLogin"]!.GetValue<string>().Should().Be("cool_user");
        sample["userDisplayName"]!.GetValue<string>().Should().Be("Cool_User");
        DateTimeOffset
            .Parse(sample["followedAt"]!.GetValue<string>())
            .Should()
            .Be(new DateTimeOffset(2026, 6, 20, 11, 29, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Every_wire_fixture_is_turned_into_its_domain_event_by_a_real_translator()
    {
        EventCatalog catalog = new();
        TranslatedEventSamples translated = TranslatedEventSamples.For(catalog);

        List<string> untranslated =
        [
            .. EventSamplePayloads
                .ByWireName.Keys.Where(wireName =>
                    !translated.TranslatedTypes.Contains(
                        catalog.Descriptors.Single(d => d.WireName == wireName).ClrType
                    )
                )
                .Order(StringComparer.Ordinal),
        ];

        untranslated
            .Should()
            .BeEmpty("each fixture must keep its real values, not fall back to a made-up sample");
    }

    [Fact]
    public void Every_catalog_event_has_a_non_null_sample_payload()
    {
        IReadOnlyList<EventCatalogItemDto> catalog = RealEmitter()
            .EmitEventCatalog(SdkContext.Script);

        catalog.Should().NotBeEmpty();
        catalog.Should().OnlyContain(c => c.SamplePayloadJson != null);
    }

    // Proves the generated/pinned sample is a faithful instance of the schema: every required property is
    // present, and every property present in the sample carries a value of the schema-declared JSON type.
    private static void AssertSampleConformsToSchema(string sampleJson, JsonNode schema)
    {
        JsonObject sample = (JsonObject)JsonNode.Parse(sampleJson)!;
        JsonObject schemaObj = (JsonObject)schema;
        JsonObject properties = (JsonObject)schemaObj["properties"]!;

        if (schemaObj["required"] is JsonArray required)
        {
            foreach (JsonNode? requiredName in required)
            {
                string name = requiredName!.GetValue<string>();
                sample
                    .ContainsKey(name)
                    .Should()
                    .BeTrue($"required property '{name}' must be present");
            }
        }

        foreach (KeyValuePair<string, JsonNode?> field in sample)
        {
            properties
                .ContainsKey(field.Key)
                .Should()
                .BeTrue($"sample property '{field.Key}' must be declared in the schema");
            if (field.Value is not null)
                AssertValueMatchesType(field.Value, (JsonObject)properties[field.Key]!, field.Key);
        }
    }

    private static void AssertValueMatchesType(
        JsonNode value,
        JsonObject propertySchema,
        string path
    )
    {
        JsonNode typeNode = propertySchema["type"]!;
        IReadOnlyList<string> allowedTypes = typeNode is JsonArray typeArray
            ? [.. typeArray.Select(t => t!.GetValue<string>())]
            : [typeNode.GetValue<string>()];

        switch (value)
        {
            case JsonObject nestedObject:
                allowedTypes.Should().Contain("object", $"'{path}' should be an object");
                if (propertySchema["properties"] is JsonObject nestedProps)
                {
                    foreach (KeyValuePair<string, JsonNode?> field in nestedObject)
                    {
                        if (field.Value is not null && nestedProps.ContainsKey(field.Key))
                            AssertValueMatchesType(
                                field.Value,
                                (JsonObject)nestedProps[field.Key]!,
                                $"{path}.{field.Key}"
                            );
                    }
                }
                break;
            case JsonArray array:
                allowedTypes.Should().Contain("array", $"'{path}' should be an array");
                if (propertySchema["items"] is JsonObject itemSchema && array.Count > 0)
                    AssertValueMatchesType(array[0]!, itemSchema, $"{path}[0]");
                break;
            case JsonValue scalar:
                AssertScalarMatchesType(scalar, allowedTypes, path);
                break;
        }
    }

    private static void AssertScalarMatchesType(
        JsonValue scalar,
        IReadOnlyList<string> allowedTypes,
        string path
    )
    {
        if (scalar.TryGetValue(out bool _))
        {
            allowedTypes.Should().Contain("boolean", $"'{path}' should be boolean");
        }
        else if (scalar.TryGetValue(out long _) || scalar.TryGetValue(out int _))
        {
            allowedTypes
                .Any(t => t is "integer" or "number")
                .Should()
                .BeTrue($"'{path}' should be numeric");
        }
        else if (scalar.TryGetValue(out double _))
        {
            allowedTypes
                .Any(t => t is "number" or "integer")
                .Should()
                .BeTrue($"'{path}' should be numeric");
        }
        else if (scalar.TryGetValue(out string? _))
        {
            allowedTypes.Should().Contain("string", $"'{path}' should be a string");
        }
    }
}
