// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using FluentAssertions;
using NomNomzBot.Application.DevPlatform;
using NomNomzBot.Application.DevPlatform.Services;
using NomNomzBot.Infrastructure.DevPlatform;

namespace NomNomzBot.Infrastructure.Tests.DevPlatform;

/// <summary>
/// The widget's <c>NomNomz.on(...)</c> surface is typed from the payload registry: an event name maps to its payload
/// interface, so a misspelled payload field is a compile error in the editor instead of a silent <c>undefined</c> on
/// stream. The widget surface hands an author no <c>any</c>.
/// </summary>
public sealed partial class SdkWidgetEventTypesTests
{
    /// <summary>A payload with documented members, like the real alert records.</summary>
    private sealed record FakeFollow(string DisplayName, int Followers);

    private sealed record FakeCustom(IReadOnlyDictionary<string, string> Fields);

    private sealed class FakeRegistry : IWidgetEventPayloadRegistry
    {
        public IReadOnlyList<WidgetEventPayloadEntry> Events { get; } =
        [
            new("follow", typeof(FakeFollow)),
            new("supporter.tip", typeof(FakeFollow)),
            new("game.lobby", null),
        ];

        public Type CustomEventPayloadType => typeof(FakeCustom);
    }

    private static string WidgetDts(IWidgetEventPayloadRegistry? registry) =>
        new SdkTypeEmitter(new EventCatalog(), null, null, registry).EmitTypeScript(
            SdkContext.Widget
        );

    // The code of the declaration file: JSDoc and comment lines are prose ("any" may appear in a sentence).
    private static IEnumerable<string> CodeLines(string dts) =>
        dts.Split('\n')
            .Select(line => line.Trim())
            .Where(line =>
                line.Length > 0
                && !line.StartsWith("//", StringComparison.Ordinal)
                && !line.StartsWith("/*", StringComparison.Ordinal)
                && !line.StartsWith('*')
            );

    [GeneratedRegex(@"\bany\b")]
    private static partial Regex AnyKeyword();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_widget_surface_declares_no_any(bool withRegistry)
    {
        string dts = WidgetDts(withRegistry ? new FakeRegistry() : null);

        List<string> offenders = [.. CodeLines(dts).Where(line => AnyKeyword().IsMatch(line))];

        offenders
            .Should()
            .BeEmpty(
                "an `any` in the widget SDK turns every payload typo into a runtime undefined: "
                    + string.Join(" | ", offenders)
            );
    }

    [Fact]
    public void An_event_name_maps_to_its_payload_interface_with_the_payloads_members()
    {
        string dts = WidgetDts(new FakeRegistry());

        dts.Should().Contain("interface NnzWidgetEventMap {");
        dts.Should().Contain("  'follow': NnzFakeFollow;");
        dts.Should().Contain("  'supporter.tip': NnzFakeFollow;");
        dts.Should().Contain("interface NnzFakeFollow {");
        dts.Should().Contain("  displayName: string;");
        dts.Should().Contain("  followers: number;");
    }

    [Fact]
    public void A_frame_with_no_single_type_is_a_free_form_record_not_any()
    {
        string dts = WidgetDts(new FakeRegistry());

        dts.Should().Contain("  'game.lobby': Record<string, unknown>;");
    }

    [Fact]
    public void on_is_generic_over_the_event_map_and_hands_the_handler_the_mapped_payload()
    {
        string dts = WidgetDts(new FakeRegistry());

        dts.Should()
            .Contain(
                "on<K extends keyof NnzWidgetEventMap>(eventType: K, handler: (data: NnzWidgetEventMap[K], eventType: K) => void): NnzOverlaySdk;"
            );
        dts.Should()
            .Contain(
                "off<K extends keyof NnzWidgetEventMap>(eventType: K, handler: (data: NnzWidgetEventMap[K], eventType: K) => void): NnzOverlaySdk;"
            );
    }

    [Fact]
    public void A_custom_event_is_typed_through_a_template_literal_overload()
    {
        string dts = WidgetDts(new FakeRegistry());

        dts.Should().Contain("on(eventType: `custom.${string}`, handler: (data: NnzFakeCustom,");
        dts.Should().Contain("interface NnzFakeCustom {");
    }

    [Fact]
    public void An_event_name_the_registry_does_not_know_falls_back_to_unknown_never_any()
    {
        string dts = WidgetDts(new FakeRegistry());

        dts.Should()
            .Contain(
                "on(eventType: string, handler: (data: unknown, eventType: string) => void): NnzOverlaySdk;"
            );
    }

    [Fact]
    public void onAny_hands_the_handler_a_union_of_event_name_and_payload_pairs()
    {
        string dts = WidgetDts(new FakeRegistry());

        dts.Should().Contain("type NnzWidgetAnyEvent =");
        dts.Should().Contain("[eventType: 'follow', data: NnzFakeFollow]");
        dts.Should()
            .Contain("onAny(handler: (...event: NnzWidgetAnyEvent) => void): NnzOverlaySdk;");
    }

    [Fact]
    public void Settings_are_unknown_not_any()
    {
        string dts = WidgetDts(null);

        dts.Should().Contain("readonly settings: Record<string, unknown>;");
        dts.Should().Contain("onSettings(handler: (settings: Record<string, unknown>) => void)");
        dts.Should().Contain("declare const WIDGET_SETTINGS: Record<string, unknown>;");
    }

    [Fact]
    public void claim_documents_when_it_rejects_and_the_action_error_code_lists_AUTH_REQUIRED()
    {
        string dts = WidgetDts(null);

        dts.Should().Contain("claim(key: string): Promise<boolean>;");
        dts.Should().Contain("rejects");
        dts.Should().Contain("not connected");
        dts.Should().Contain("AUTH_REQUIRED");
        dts.Should().Contain("VALIDATION_FAILED");
    }
}
