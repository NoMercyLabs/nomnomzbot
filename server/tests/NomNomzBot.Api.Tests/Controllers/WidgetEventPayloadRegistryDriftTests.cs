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
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Application.DevPlatform;
using NomNomzBot.Application.DevPlatform.Services;
using NomNomzBot.Infrastructure.DevPlatform;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Holds the widget payload registry (the source of the typed <c>NomNomz.on(...)</c> declarations) to what the bot
/// really sends: every test-fire sample, and every event name a broadcaster pushes to a widget. A drift here means
/// the editor types a payload the stream does not send.
/// </summary>
public sealed partial class WidgetEventPayloadRegistryDriftTests
{
    // The only free-form event: the generic "test" one, whose payload a widget defines itself.
    private static readonly string[] SdkLocalEvents = ["test"];

    private static readonly WidgetEventPayloadRegistry Registry = new();

    private static string BroadcastersFolder()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(
                dir.FullName,
                "src",
                "NomNomzBot.Api",
                "Hubs",
                "Broadcasters"
            );
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            $"Could not locate the Broadcasters folder above '{AppContext.BaseDirectory}'."
        );
    }

    [Fact]
    public void Every_sample_has_a_registry_entry_of_the_samples_runtime_type()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<string> mismatches = [];

        foreach (string name in WidgetTestSamples.EventTypes)
        {
            Type sampleType = WidgetTestSamples.For(name, now).GetType();
            Type? expected;
            IReadOnlyList<Type>? variants = null;
            if (name.StartsWith("custom.", StringComparison.Ordinal))
            {
                expected = Registry.CustomEventPayloadType;
            }
            else
            {
                WidgetEventPayloadEntry? entry = Registry.Events.FirstOrDefault(e =>
                    e.Name == name
                );
                if (entry is null)
                {
                    mismatches.Add($"{name}: not in the registry");
                    continue;
                }
                expected = entry.PayloadType;
                variants = entry.Variants;
            }

            // A null registry type is a free-form frame; its sample is an anonymous object. A variant list means
            // the sample must be one of the frame records.
            bool matches =
                variants is not null ? variants.Contains(sampleType)
                : expected is null
                    ? sampleType.Name.Contains("AnonymousType", StringComparison.Ordinal)
                : sampleType == expected;
            if (!matches)
                mismatches.Add(
                    $"{name}: sample is {sampleType.Name}, registry says {(variants is not null ? string.Join(" | ", variants.Select(v => v.Name)) : expected?.Name ?? "free-form")}"
                );
        }

        mismatches.Should().BeEmpty(string.Join("; ", mismatches));
    }

    [Fact]
    public void No_registered_widget_event_has_a_null_payload_type()
    {
        // Only the generic "test" event may stay free-form.
        List<string> untyped =
        [
            .. Registry
                .Events.Where(e => e.PayloadType is null && e.Variants is null)
                .Select(e => e.Name)
                .Where(n => !SdkLocalEvents.Contains(n)),
        ];

        untyped
            .Should()
            .BeEmpty(
                "these widget events reach a widget as Record<string, unknown>: "
                    + string.Join(", ", untyped)
            );
    }

    [Fact]
    public void The_registry_holds_exactly_the_sampled_events()
    {
        List<string> sampled =
        [
            .. WidgetTestSamples.EventTypes.Where(n =>
                !n.StartsWith("custom.", StringComparison.Ordinal)
            ),
        ];

        Registry.Events.Select(e => e.Name).Should().BeEquivalentTo(sampled);
        Registry.Events.Select(e => e.Name).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Every_event_name_a_broadcaster_sends_is_in_the_registry()
    {
        List<string> sent = [];
        foreach (string file in Directory.EnumerateFiles(BroadcastersFolder(), "*.cs"))
        {
            string text = File.ReadAllText(file);
            sent.AddRange(OverlayAlertCall().Matches(text).Select(m => m.Groups["name"].Value));
            foreach (Match route in RouteCall().Matches(text))
                sent.Add(ResolveRouteName(route.Groups["name"].Value, text));
        }

        // Sanity: the scan really found the calls, so an empty list can never pass.
        sent.Should().Contain(["follow", "ChatMessage", "sr_queue", "tts_speak", "now_playing"]);

        List<string> unregistered =
        [
            .. sent.Distinct(StringComparer.Ordinal)
                .Where(name =>
                    name.Length > 0
                    && !name.StartsWith("custom.", StringComparison.Ordinal)
                    && Registry.Events.All(e => e.Name != name)
                ),
        ];
        unregistered
            .Should()
            .BeEmpty(
                "a broadcaster sends events the widget types never declare: "
                    + string.Join(", ", unregistered)
            );
    }

    [Fact]
    public void The_real_emitter_writes_the_real_payload_members_into_the_event_map()
    {
        string dts = new SdkTypeEmitter(new EventCatalog(), null, null, Registry).EmitTypeScript(
            SdkContext.Widget
        );

        dts.Should().Contain("  'follow': NnzFollowAlertDto;");
        dts.Should().Contain("interface NnzFollowAlertDto {");
        dts.Should().Contain("  displayName: string;");
        dts.Should().MatchRegex(@"  'game\.lobby': Nnz\w+( \| Nnz\w+)+;");
        dts.Should().NotContain("'game.lobby': Record<string, unknown>");
        dts.Should()
            .Contain(
                "on(eventType: `custom.${string}`, handler: (data: NnzCustomDataWidgetPayload,"
            );
        dts.Should().Contain("  fields: Record<string, string>;");
    }

    [Theory]
    [InlineData("youtube.pause")]
    [InlineData("youtube.resume")]
    [InlineData("youtube.stop")]
    [InlineData("youtube.seek")]
    public void The_youtube_transport_events_are_registered_sampled_and_typed_in_the_event_map(
        string eventType
    )
    {
        WidgetEventPayloadEntry? entry = Registry.Events.FirstOrDefault(e => e.Name == eventType);
        entry.Should().NotBeNull();
        entry.PayloadType.Should().NotBeNull();
        WidgetTestSamples.EventTypes.Should().Contain(eventType);
        WidgetTestSamples
            .For(eventType, DateTimeOffset.UnixEpoch)
            .GetType()
            .Should()
            .Be(entry.PayloadType);

        string dts = new SdkTypeEmitter(new EventCatalog(), null, null, Registry).EmitTypeScript(
            SdkContext.Widget
        );

        dts.Should().Contain($"  '{eventType}': Nnz{entry.PayloadType!.Name};");
    }

    /// <summary>The eventType argument (first literal, no other quotes before it) of an overlay alert push.</summary>
    [GeneratedRegex(@"OverlayAlertBroadcast\s*\.\s*ToOverlaysAsync\(\s*[^""]*?""(?<name>[^""]+)""")]
    private static partial Regex OverlayAlertCall();

    /// <summary>The eventType argument (fourth) of a widget route call.</summary>
    [GeneratedRegex(
        @"WidgetAlertDispatch\s*\.\s*RouteAsync\(\s*[^,]+,\s*[^,]+,\s*[^,]+,\s*(?<name>\$?""[^""]+""|[A-Za-z_.]+)\s*,"
    )]
    private static partial Regex RouteCall();

    [GeneratedRegex(@"WidgetEventType\s*=\s*""(?<value>[^""]+)""")]
    private static partial Regex WidgetEventTypeConst();

    private static string ResolveRouteName(string argument, string fileText)
    {
        if (argument.StartsWith("$\"", StringComparison.Ordinal))
            return argument.Trim('$', '"').Split('{')[0];
        if (argument.StartsWith('"'))
            return argument.Trim('"');

        // A forwarded parameter (OverlayAlertBroadcast passes its own eventType on) names no event itself.
        Match constant = WidgetEventTypeConst().Match(fileText);
        return argument == "WidgetEventType" && constant.Success
            ? constant.Groups["value"].Value
            : string.Empty;
    }
}
