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
        // Only a widget page has an event map: a script has no event bus.
        string ts = RealEmitter().EmitTypeScript(SdkContext.Widget);

        // The map entry keys the stable wire name to the reflected payload interface.
        ts.Should().Contain("'chat.message': NnzChatMessageReceived;");

        // The payload interface carries the reflected, correctly-typed fields (string / number / boolean / array).
        ts.Should().Contain("interface NnzChatMessageReceived {");
        ts.Should().Contain("messageId: string;");
        ts.Should().Contain("bits: number;");
        ts.Should().Contain("isSubscriber: boolean;");
        ts.Should().Contain("fragments: NnzChatMessageFragment[];");

        // Wire names are the map's keys, so the whole catalogue is addressable by its stable name.
        ts.Should().Contain("interface NnzEventMap {");
        ts.Should().Contain("'stream.online': NnzChannelOnline;");
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
            ts.Should().Contain("interface NnzPiiSample {");
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
        // Normalized to \n: SdkRuntimeSurface builds the .d.ts with StringBuilder.AppendLine, which emits
        // Environment.NewLine (\r\n on Windows) — the raw-string blocks below are \n-only, so without this the
        // multi-line Contain assertions fail on an invisible CRLF/LF mismatch despite matching text.
        string ts = RealEmitter().EmitTypeScript(SdkContext.Script).ReplaceLineEndings("\n");

        // Batteries — the pure-JS floor, with real signatures (not just a name) — each its own named
        // interface (not one sprawling inline object) so hovering `nnz.time` etc. shows a short, readable type.
        ts.Should().Contain("interface NnzUnits {");
        ts.Should().Contain("convert(value: number, from: string, to: string): number;");
        ts.Should().Contain("interface NnzTime {");
        ts.Should().Contain("sleep(ms: number): void;");
        ts.Should().Contain("interface NnzStr {");
        ts.Should().Contain("slugify(value: string): string;");
        ts.Should().Contain("interface NnzMath {");
        ts.Should().Contain("randomInt(min: number, max: number): number;");
        ts.Should()
            .Contain(
                """
                declare const nnz: {
                  /** The error of this script's last failed host call. null when it did not fail. Needs no grant. */
                  readonly lastError: NnzApiError | null;
                  /** Unit conversion. */
                  units: NnzUnits;
                  /** Date and time helpers. */
                  time: NnzTime;
                  /** Number helpers. */
                  math: NnzMath;
                  /** Text helpers. */
                  str: NnzStr;
                  /** JSON helpers that never throw. */
                  json: NnzJson;
                  /** Random value helpers. */
                  random: NnzRandom;
                  /** The calls to the bot: chat, music, storage, TTS, rewards and more. Each call needs its grant. */
                  api: NnzApi;
                };
                """
            );

        // The typed api wrappers, including the write/privileged surface — same one-interface-per-namespace
        // shape, grouped under NnzApi — and their return interfaces.
        ts.Should()
            .Contain(
                """
                interface NnzApiChatNamespace {
                  /** Says a line in chat as the bot. */
                  send(text: string): void;
                  /** Answers the chat message that started this script, as a threaded reply. If the platform
                   *  refuses the thread, it mentions the viewer instead. With no chat message, it says a normal line. */
                  reply(text: string): void;
                }
                """
            );
        ts.Should()
            .Contain(
                """
                interface NnzApiHttpNamespace {
                  /** Fetches an https URL and returns the response body as text. Returns null when the URL is not https, the request is blocked or the server does not answer with success. */
                  fetch(url: string): string | null;
                }
                """
            );
        ts.Should().Contain("queue(uri: string): boolean");
        ts.Should().Contain("get(id?: string): NnzApiUser | null");
        ts.Should().Contain("interface NnzApiUser {");
        ts.Should().Contain("interface NnzApiTrack {");
        ts.Should()
            .Contain(
                """
                interface NnzApi {
                  /** Runs a pipeline action by its type. */
                  actions: NnzApiActionsNamespace;
                  /** Read a viewer's public profile. */
                  user: NnzApiUserNamespace;
                  /** Read a viewer's currency balance. */
                  economy: NnzApiEconomyNamespace;
                  /** Send chat messages as the bot. */
                  chat: NnzApiChatNamespace;
                  /** Read the playing track and request songs. */
                  music: NnzApiMusicNamespace;
                  /** Fetch an https URL. */
                  http: NnzApiHttpNamespace;
                  /** Per-channel storage that persists between runs. */
                  storage: NnzApiStorageNamespace;
                  /** Speak text on the overlay and manage viewer voices. */
                  tts: NnzApiTtsNamespace;
                  /** Read a viewer's channel stats. */
                  stats: NnzApiStatsNamespace;
                  /** Send events to this channel's widgets. */
                  widget: NnzApiWidgetNamespace;
                  /** Read and update channel-point rewards. */
                  reward: NnzApiRewardNamespace;
                  /** Run a saved pipeline later. */
                  schedule: NnzApiScheduleNamespace;
                }
                """
            );

        // The storage / tts / widget / reward groups with their typed signatures + payload interfaces.
        ts.Should()
            .Contain(
                """
                interface NnzApiStorageNamespace {
                  /** The stored value for the key. null when nothing is stored under it. */
                  get(key: string): string | null;
                  /** Stores a text value under the key. Returns false when the write is refused, for example over the size limit. */
                  set(key: string, value: string): boolean;
                  /** Deletes the key. Returns true when the call succeeded. */
                  delete(key: string): boolean;
                  /** The stored keys. prefix is optional and keeps only keys that start with it. An empty list when none match. */
                  list(prefix?: string): string[];
                }
                """
            );
        ts.Should()
            .Contain(
                """
                interface NnzApiTtsNamespace {
                  /** Leave voiceId, ratePercent or pitchPercent undefined to keep the normal value. Null when TTS refused the line. */
                  speak(text: string, voiceId?: string, ratePercent?: number, pitchPercent?: number): NnzApiTtsResult | null;
                  /** The voice assigned to a viewer. userIdOrLogin is optional and defaults to the user who triggered the script. null when the viewer uses the channel default or no viewer matches. */
                  getVoice(userIdOrLogin?: string): NnzApiTtsVoice | null;
                  /** Assigns a voice to a viewer. voiceId is optional: leave it out to clear back to the channel default. Returns false when it fails. */
                  setVoice(userIdOrLogin: string, voiceId?: string): boolean;
                }
                """
            );
        ts.Should()
            .Contain(
                """
                interface NnzApiStatsNamespace {
                  /** A viewer's channel stats. userIdOrLogin is optional and defaults to the user who triggered the script. A viewer never seen gets zeros. */
                  viewer(userIdOrLogin?: string): NnzApiViewerStats;
                }
                """
            );
        ts.Should()
            .Contain(
                """
                interface NnzApiWidgetNamespace {
                  /** Sends an event to a widget. data is optional. Returns false when no widget matches, the widget is turned off or the send fails. */
                  emit(widgetIdOrName: string, eventType: string, data?: unknown): boolean;
                }
                """
            );
        ts.Should()
            .Contain(
                """
                interface NnzApiRewardNamespace {
                  /** A channel-point reward, found by id or title. null when no reward matches. */
                  get(rewardIdOrTitle: string): NnzApiReward | null;
                  /** Applies the patch to a reward. Returns false when no reward matches, the bot cannot manage it or the update fails. */
                  update(rewardIdOrTitle: string, patch: NnzApiRewardPatch): boolean;
                }
                """
            );
        ts.Should().Contain("interface NnzApiTtsResult {");
        ts.Should().Contain("interface NnzApiTtsVoice {");
        ts.Should().Contain("interface NnzApiViewerStats {");
        ts.Should().Contain("interface NnzApiReward {");
        ts.Should().Contain("interface NnzApiRewardPatch {");
    }

    [Fact]
    public void Widget_dts_declares_the_browser_globals_and_nothing_from_the_script_sandbox()
    {
        string ts = RealEmitter().EmitTypeScript(SdkContext.Widget);

        // A widget page has exactly one SDK object — window.NomNomz from /overlay/sdk.js — plus the config the
        // host page injects. Signatures, not bare names: every registration returns the SDK so calls chain.
        ts.Should().Contain("declare const NomNomz: NnzOverlaySdk;");
        ts.Should()
            .Contain(
                "  on(eventType: string, handler: (data: unknown, eventType: string) => void): NnzOverlaySdk;"
            );
        ts.Should()
            .Contain(
                "  onAny(handler: (eventType: string, data: unknown) => void): NnzOverlaySdk;"
            );
        ts.Should()
            .Contain(
                "  onSettings(handler: (settings: Record<string, unknown>) => void): NnzOverlaySdk;"
            );
        ts.Should().Contain("  reportError(message: string): void;");
        ts.Should().Contain("  readonly actions: NnzOverlayActions;");
        ts.Should()
            .Contain(
                "  invoke<T extends NnzActionsWithOptionalParams>(actionType: T, params?: NnzActionParams[T], variables?: Record<string, string | number>): Promise<NnzActionResult>;"
            );
        ts.Should().Contain("  claim(key: string): Promise<boolean>;");
        ts.Should().Contain("  readonly settings: Record<string, unknown>;");
        ts.Should().Contain("declare const WIDGET_ID: string;");
        ts.Should().Contain("declare const WIDGET_TOKEN: string;");
        ts.Should().Contain("declare const WIDGET_NAME: string;");
        ts.Should().Contain("declare const WIDGET_SETTINGS: Record<string, unknown>;");
        ts.Should().Contain("declare const WIDGET_EVENT_SUBSCRIPTIONS: string[];");

        // There is no `nnz` in a browser — no capability broker, so neither the batteries nor ANY of the api.
        ts.Should().NotContain("declare const nnz");
        ts.Should().NotContain("declare const bot");
        ts.Should().NotContain("convert(value: number, from: string, to: string): number;");
        ts.Should().NotContain("NnzApiUser");
        ts.Should().NotContain("NnzApiTrack");
        ts.Should().NotContain("chat: {");
        ts.Should().NotContain("http: {");
        ts.Should().NotContain("storage: {");
        ts.Should().NotContain("reward: {");
    }

    [Fact]
    public void Adding_the_fixed_surface_does_not_regress_reflected_event_types()
    {
        // The keystone invariant: events stay 100%-auto-reflected from the C# records even though the fixed
        // SDK surface is now authored alongside them.
        string ts = RealEmitter().EmitTypeScript(SdkContext.Script);

        ts.Should().Contain("interface NnzChatMessageReceived {");
        RealEmitter()
            .EmitTypeScript(SdkContext.Widget)
            .Should()
            .Contain("'chat.message': NnzChatMessageReceived;");

        // …and the authored globals sit alongside them, untouched by the reflection pass.
        ts.Should().Contain("declare const bot: {");
        ts.Should().Contain("declare const nnz: {");
    }
}
