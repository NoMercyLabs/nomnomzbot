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
using NomNomzBot.Domain.Platform;
using NomNomzBot.Infrastructure.DevPlatform;

namespace NomNomzBot.Infrastructure.Tests.DevPlatform;

/// <summary>
/// Proves the reflection emitter's generated artifacts (dev-platform.md §1.3, §2, §3.1): the real chat-message
/// event lands in <c>NnzEventMap</c> with its typed payload; <c>[Pii]</c> is kept for the script context and
/// stripped from the widget context; <c>[NotExposed]</c> never appears; an Internal-tier event appears in no
/// context; enums become string-literal unions; and the JSON-schema catalog carries the same shape and tier.
/// </summary>
public sealed class SdkTypeEmitterTests
{
    private static SdkTypeEmitter RealEmitter() => new(new EventCatalog());

    private static SdkTypeEmitter FakeEmitter(params EventDescriptor[] descriptors) =>
        new(new FakeEventCatalog(descriptors));

    [Fact]
    public void Script_dts_lands_the_chat_message_event_in_the_event_map_with_typed_payload()
    {
        string ts = RealEmitter().EmitTypeScript(SdkContext.Script);

        // The map entry keys the stable wire name to the reflected payload interface.
        ts.Should().Contain("'chat.message': NnzChatMessageReceived;");

        // The payload interface carries the reflected, correctly-typed fields (string / number / boolean / array).
        ts.Should().Contain("interface NnzChatMessageReceived {");
        ts.Should().Contain("messageId: string;");
        ts.Should().Contain("bits: number;");
        ts.Should().Contain("isSubscriber: boolean;");
        ts.Should().Contain("fragments: NnzChatMessageFragment[];");

        // The typed nomercy-player-core surface is emitted verbatim in shape.
        ts.Should()
            .Contain(
                "on<K extends keyof NnzEventMap>(event: K, fn: (data: NnzEventMap[K]) => void): void;"
            );
    }

    [Fact]
    public void Pii_field_is_present_for_script_and_absent_for_widget()
    {
        SdkTypeEmitter emitter = FakeEmitter(
            new EventDescriptor(
                "nnztest.pii.sample",
                EventVisibility.Public,
                typeof(PiiSampleEvent)
            )
        );

        string script = emitter.EmitTypeScript(SdkContext.Script);
        string widget = emitter.EmitTypeScript(SdkContext.Widget);

        script.Should().Contain("normal: string;");
        script.Should().Contain("secretEmail?: string | null;", "script keeps [Pii] fields");

        widget.Should().Contain("normal: string;", "the non-PII field still appears for widgets");
        widget
            .Should()
            .NotContain("secretEmail", "[Pii] is stripped from the untrusted widget context");
    }

    [Fact]
    public void NotExposed_field_never_appears_in_either_context()
    {
        SdkTypeEmitter emitter = FakeEmitter(
            new EventDescriptor(
                "nnztest.pii.sample",
                EventVisibility.Public,
                typeof(PiiSampleEvent)
            )
        );

        emitter.EmitTypeScript(SdkContext.Script).Should().NotContain("internalId");
        emitter.EmitTypeScript(SdkContext.Widget).Should().NotContain("internalId");
    }

    [Fact]
    public void Internal_tier_event_appears_in_no_context()
    {
        SdkTypeEmitter emitter = FakeEmitter(
            new EventDescriptor(
                "nnztest.internal.sample",
                EventVisibility.Internal,
                typeof(InternalSampleEvent)
            ),
            new EventDescriptor(
                "nnztest.pii.sample",
                EventVisibility.Public,
                typeof(PiiSampleEvent)
            )
        );

        foreach (SdkContext context in new[] { SdkContext.Script, SdkContext.Widget })
        {
            string ts = emitter.EmitTypeScript(context);
            ts.Should().NotContain("nnztest.internal.sample");
            ts.Should().NotContain("NnzInternalSample");
            ts.Should().NotContain("whatever");
            // The Public sibling still comes through, so the absence is the tier filter, not an empty emit.
            ts.Should().Contain("nnztest.pii.sample");
        }
    }

    [Fact]
    public void Enum_property_becomes_a_string_literal_union()
    {
        string ts = FakeEmitter(
                new EventDescriptor(
                    "nnztest.pii.sample",
                    EventVisibility.Public,
                    typeof(PiiSampleEvent)
                )
            )
            .EmitTypeScript(SdkContext.Script);

        ts.Should().Contain("color: 'Red' | 'Green' | 'Blue';");
    }

    [Fact]
    public void Nested_record_and_collection_map_to_an_interface_array()
    {
        string ts = FakeEmitter(
                new EventDescriptor(
                    "nnztest.pii.sample",
                    EventVisibility.Public,
                    typeof(PiiSampleEvent)
                )
            )
            .EmitTypeScript(SdkContext.Script);

        ts.Should().Contain("items: NnzSdkFixtureNested[];");
        ts.Should().Contain("interface NnzSdkFixtureNested {");
        ts.Should().Contain("label: string;");
        ts.Should().Contain("count: number;");
    }

    [Fact]
    public void Event_catalog_item_carries_wire_name_tier_and_a_typed_payload_schema()
    {
        IReadOnlyList<EventCatalogItemDto> catalog = RealEmitter()
            .EmitEventCatalog(SdkContext.Script);

        EventCatalogItemDto chat = catalog.Single(c => c.WireName == "chat.message");
        chat.Tier.Should().Be("Public");

        JsonObject properties = (JsonObject)chat.PayloadSchema["properties"]!;
        properties["messageId"]!["type"]!.GetValue<string>().Should().Be("string");
        properties["bits"]!["type"]!.GetValue<string>().Should().Be("integer");
        chat.PayloadSchema["type"]!.GetValue<string>().Should().Be("object");
    }

    [Fact]
    public void Event_catalog_schema_respects_pii_and_required_per_context()
    {
        SdkTypeEmitter emitter = FakeEmitter(
            new EventDescriptor(
                "nnztest.pii.sample",
                EventVisibility.Public,
                typeof(PiiSampleEvent)
            )
        );

        JsonObject scriptProps = (JsonObject)
            emitter.EmitEventCatalog(SdkContext.Script).Single().PayloadSchema["properties"]!;
        JsonObject widgetProps = (JsonObject)
            emitter.EmitEventCatalog(SdkContext.Widget).Single().PayloadSchema["properties"]!;

        scriptProps.ContainsKey("secretEmail").Should().BeTrue("script schema keeps PII");
        widgetProps.ContainsKey("secretEmail").Should().BeFalse("widget schema strips PII");
        widgetProps.ContainsKey("internalId").Should().BeFalse("[NotExposed] never in a schema");

        // Non-nullable is required; a nullable field is optional.
        JsonArray required = (JsonArray)
            emitter.EmitEventCatalog(SdkContext.Script).Single().PayloadSchema["required"]!;
        List<string> requiredNames = [.. required.Select(n => n!.GetValue<string>())];
        requiredNames.Should().Contain("normal");
        requiredNames.Should().NotContain("optionalNote");
    }

    [Fact]
    public void Script_dts_declares_the_batteries_and_full_api_surface()
    {
        string ts = RealEmitter().EmitTypeScript(SdkContext.Script);

        // Batteries — the pure-JS floor, with real signatures (not just a name).
        ts.Should().Contain("convert(value: number, from: string, to: string): number;");
        ts.Should().Contain("slugify(value: string): string;");
        ts.Should().Contain("randomInt(min: number, max: number): number;");

        // The typed api wrappers, including the write/privileged surface, and their return interfaces.
        ts.Should().Contain("chat: { send(text: string): void; reply(text: string): void };");
        ts.Should().Contain("http: { fetch(url: string): string | null };");
        ts.Should().Contain("queue(uri: string): boolean");
        ts.Should().Contain("get(id?: string): NnzApiUser | null");
        ts.Should().Contain("interface NnzApiUser {");
        ts.Should().Contain("interface NnzApiTrack {");

        // The storage / tts / widget / reward groups with their typed signatures + payload interfaces.
        ts.Should()
            .Contain(
                "storage: { get(key: string): string | null; set(key: string, value: string): boolean; delete(key: string): boolean; list(prefix?: string): string[] };"
            );
        ts.Should()
            .Contain("tts: { speak(text: string, voiceId?: string): NnzApiTtsResult | null };");
        ts.Should()
            .Contain(
                "widget: { emit(widgetIdOrName: string, eventType: string, data?: unknown): boolean };"
            );
        ts.Should()
            .Contain(
                "reward: { get(rewardIdOrTitle: string): NnzApiReward | null; update(rewardIdOrTitle: string, patch: NnzApiRewardPatch): boolean };"
            );
        ts.Should().Contain("interface NnzApiTtsResult {");
        ts.Should().Contain("interface NnzApiReward {");
        ts.Should().Contain("interface NnzApiRewardPatch {");
    }

    [Fact]
    public void Widget_dts_keeps_the_batteries_and_read_api_but_omits_the_script_only_api()
    {
        string ts = RealEmitter().EmitTypeScript(SdkContext.Widget);

        // Batteries are available everywhere.
        ts.Should().Contain("convert(value: number, from: string, to: string): number;");
        // The read-mostly api is available to widgets.
        ts.Should().Contain("get(id?: string): NnzApiUser | null");
        ts.Should().Contain("nowPlaying(): NnzApiTrack | null");

        // The write/privileged api is script-only — it must not appear in the untrusted widget surface.
        ts.Should().NotContain("chat: {");
        ts.Should().NotContain("http: {");
        ts.Should().NotContain("queue(uri: string): boolean");
        ts.Should().NotContain("storage: {");
        ts.Should().NotContain("tts: {");
        ts.Should().NotContain("widget: {");
        ts.Should().NotContain("reward: {");
    }

    [Fact]
    public void Adding_the_fixed_surface_does_not_regress_reflected_event_types()
    {
        // The keystone invariant: events stay 100%-auto-reflected from the C# records even though the fixed
        // SDK surface is now authored alongside them.
        string ts = RealEmitter().EmitTypeScript(SdkContext.Script);

        ts.Should().Contain("'chat.message': NnzChatMessageReceived;");
        ts.Should().Contain("interface NnzChatMessageReceived {");
        ts.Should()
            .Contain(
                "on<K extends keyof NnzEventMap>(event: K, fn: (data: NnzEventMap[K]) => void): void;"
            );
    }
}
