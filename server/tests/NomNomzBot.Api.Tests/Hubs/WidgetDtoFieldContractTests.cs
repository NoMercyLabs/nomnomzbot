// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Infrastructure.Content.Widgets;
using NomNomzBot.Infrastructure.Widgets.EventHandlers;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// The systemic bug the widget-quality audit (§1) found: a widget `.vue` script reads an event-payload field
/// name that doesn't match the real, camelCase-serialized DTO the backend actually broadcasts (<c>AlertDtos.cs</c>)
/// — e.g. <c>d.user</c> against a DTO that only ever sends <c>displayName</c>. The mismatch is silent (a plain
/// string-keyed object access, `undefined` swallowed by a `||` fallback), so nothing catches it until someone
/// watches a live event render as a placeholder.
///
/// This is a data-driven contract test, table-driven over {widget, handler function, DTO(s) it receives}: for
/// every row it (1) reflects the real DTO type(s)' public properties and applies the same
/// <see cref="JsonNamingPolicy.CamelCase"/> the API's JSON options use, then (2) extracts the named handler
/// function's body from the real `.vue` source and collects every <c>d.&lt;field&gt;</c> access in it, and
/// (3) asserts every accessed field is a real field on the DTO(s) (plus each row's small, explicit allowance for
/// an intentional legacy-fallback read or an out-of-scope event family with no first-party DTO yet — supporter.*
/// events). Add a widget to <see cref="Rows"/> and it is covered automatically; nothing else to wire.
/// </summary>
public sealed class WidgetDtoFieldContractTests
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public sealed record Row(
        string WidgetName,
        string FunctionName,
        Type[] DtoTypes,
        string[] ExtraAllowedFields
    )
    {
        public Row(string widgetName, string functionName, params Type[] dtoTypes)
            : this(widgetName, functionName, dtoTypes, []) { }
    }

    // The single source of truth for the theory data AND the coverage-discovery check below — a TheoryData<T>
    // instance is write-only-ish to enumerate back out in xunit 2.x, so the raw rows live here once.
    private static readonly Row[] AllRows =
    [
        new("recent_followers", "onFollow", typeof(FollowAlertDto)),
        new("top_cheerers", "onCheer", typeof(CheerAlertDto)),
        new("sub_train", "onGift", typeof(GiftSubAlertDto)),
        new("labels", "onFollow", typeof(FollowAlertDto)),
        new("labels", "onSub", typeof(SubscriptionAlertDto)),
        new("labels", "onResub", typeof(ResubAlertDto)),
        new("labels", "onGift", typeof(GiftSubAlertDto)),
        new("labels", "onCheer", typeof(CheerAlertDto)),
        new("goal_bar", "onGift", typeof(GiftSubAlertDto)),
        new("goal_bar", "onCheer", typeof(CheerAlertDto)),
        new(
            "alerts",
            "nameOf",
            [
                typeof(FollowAlertDto),
                typeof(SubscriptionAlertDto),
                typeof(ResubAlertDto),
                typeof(GiftSubAlertDto),
                typeof(CheerAlertDto),
                typeof(RaidAlertDto),
                typeof(SupporterAlertPayload),
            ],
            // Deliberate legacy-compatibility fallback (nameOf: `d.displayName || d.user`) — not a mismatch.
            ["user"]
        ),
        new(
            "alerts",
            "cardFor",
            [
                typeof(FollowAlertDto),
                typeof(SubscriptionAlertDto),
                typeof(ResubAlertDto),
                typeof(GiftSubAlertDto),
                typeof(CheerAlertDto),
                typeof(RaidAlertDto),
                // supporter.tip/membership/merch/charity now route through the real
                // SupporterWidgetEventHandler -> SupporterAlertPayload (S058b) — no more guessed shape.
                typeof(SupporterAlertPayload),
            ]
        ),
        new(
            "event_ticker",
            "nameOf",
            [
                typeof(FollowAlertDto),
                typeof(SubscriptionAlertDto),
                typeof(ResubAlertDto),
                typeof(GiftSubAlertDto),
                typeof(CheerAlertDto),
                typeof(RaidAlertDto),
                typeof(SupporterAlertPayload),
            ],
            ["user"]
        ),
        new(
            "event_ticker",
            "chipFor",
            [
                typeof(FollowAlertDto),
                typeof(SubscriptionAlertDto),
                typeof(ResubAlertDto),
                typeof(GiftSubAlertDto),
                typeof(CheerAlertDto),
                typeof(RaidAlertDto),
                typeof(SupporterAlertPayload),
            ]
        ),
        new(
            "redemption_alert",
            "onRedeemed",
            [
                // RewardRedeemedBroadcastHandler pushes RewardRedeemedDto straight through OverlayAlertBroadcast
                // (Api/Hubs/Dtos/HubResponseDtos.cs), not an AlertDtos.cs record.
                typeof(RewardRedeemedDto),
            ]
        ),
    ];

    public static TheoryData<Row> Rows
    {
        get
        {
            TheoryData<Row> data = new();
            foreach (Row row in AllRows)
                data.Add(row);
            return data;
        }
    }

    /// <summary>
    /// The event-type vocabulary this contract-test suite actually knows how to verify a widget's field reads
    /// against — every event type with a real DTO/payload-backed widget push: the alert-broadcast handlers'
    /// <c>FirstPartyWidgetCatalogue.SupporterAndTwitchEvents</c> (follow/subscription/resub/gift/cheer/raid/
    /// supporter.*), <c>GoalWidgetEventHandler</c>'s <c>goal</c>, and <c>RewardRedeemedBroadcastHandler</c>'s
    /// <c>reward_redeemed</c>. Reused, not retyped, from the catalogue's own constant so this is not a second
    /// hand-maintained event list.
    /// </summary>
    private static readonly HashSet<string> KnownAlertShapedEventTypes = new(
        FirstPartyWidgetCatalogue.SupporterAndTwitchEvents.Concat(["goal", "reward_redeemed"]),
        StringComparer.Ordinal
    );

    /// <summary>
    /// Discovers, from <see cref="FirstPartyWidgetCatalogue"/> itself — not a second hand-maintained list — every
    /// first-party widget that subscribes to at least one alert-shaped event type by default. A game/economy
    /// widget (drop_game, sr_queue, tts_caption, …) that never reads an alert-family payload is legitimately out
    /// of this suite's scope; one that DOES subscribe to an alert-shaped event and has zero contract rows is a
    /// real gap.
    /// </summary>
    private static IEnumerable<string> DiscoverInScopeWidgetKeys() =>
        FirstPartyWidgetCatalogue
            .All.Where(w => w.DefaultEventSubscriptions.Any(KnownAlertShapedEventTypes.Contains))
            .Select(w => w.Key);

    /// <summary>
    /// The widget-name coverage check as a pure function so it can be exercised against a synthetic fixture
    /// below, independent of xUnit's TheoryData plumbing.
    /// </summary>
    private static IReadOnlySet<string> WidgetsMissingAContractRow(
        IEnumerable<string> discoveredWidgetKeys,
        IEnumerable<Row> rows
    ) =>
        discoveredWidgetKeys
            .Except(rows.Select(r => r.WidgetName), StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The drift guard's drift guard: <see cref="AllRows"/> is hand-written, so a widget added to
    /// <see cref="FirstPartyWidgetCatalogue"/> tomorrow with an alert-shaped subscription and no matching row
    /// would silently ship with no field-contract coverage at all — the exact "guarded only what someone
    /// remembered" failure mode a static list has. This discovers the in-scope widget set from the catalogue
    /// (not a second hand-maintained list) and fails the build the moment one has zero rows.
    /// </summary>
    [Fact]
    public void Every_in_scope_first_party_widget_has_at_least_one_contract_row()
    {
        string[] discovered = [.. DiscoverInScopeWidgetKeys()];
        discovered
            .Should()
            .NotBeEmpty("at least one first-party widget subscribes to an alert-shaped event");

        IReadOnlySet<string> missing = WidgetsMissingAContractRow(discovered, AllRows);

        missing
            .Should()
            .BeEmpty(
                "every first-party widget with an alert-shaped default subscription must have at least one "
                    + "WidgetDtoFieldContractTests.AllRows entry — a widget shipped with none of its "
                    + $"event-payload field reads ever checked against a real DTO: {string.Join(", ", missing)}"
            );
    }

    /// <summary>
    /// Proves the discovery guard actually fires rather than trivially passing — a deliberate fixture widget
    /// set containing one name absent from any real row must be reported missing.
    /// </summary>
    [Fact]
    public void WidgetsMissingAContractRow_flags_a_widget_with_no_declared_row()
    {
        string[] fixtureCatalogue =
        [
            "recent_followers", // has a row — must NOT be reported
            "a_new_widget_nobody_wrote_a_contract_row_for", // has none — must be reported
        ];

        IReadOnlySet<string> missing = WidgetsMissingAContractRow(fixtureCatalogue, AllRows);

        missing.Should().BeEquivalentTo(["a_new_widget_nobody_wrote_a_contract_row_for"]);
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void Widget_handler_reads_only_fields_the_real_dto_sends(Row row)
    {
        HashSet<string> wireFields = row
            .DtoTypes.SelectMany(WireFieldNames)
            .Concat(row.ExtraAllowedFields)
            .ToHashSet(StringComparer.Ordinal);

        string vueSource = File.ReadAllText(WidgetAssetPaths.VueFile(row.WidgetName));
        string body = ExtractFunctionBody(vueSource, row.FunctionName);
        HashSet<string> referencedFields = Regex
            .Matches(body, @"(?<![\w.])d\.(\w+)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        referencedFields
            .Should()
            .NotBeEmpty(
                $"{row.WidgetName}.vue's {row.FunctionName}() should reference the event payload"
            );
        referencedFields
            .Should()
            .BeSubsetOf(
                wireFields,
                $"{row.WidgetName}.vue's {row.FunctionName}() must only read fields the real DTO(s) "
                    + $"({string.Join(", ", row.DtoTypes.Select(t => t.Name))}) actually send"
            );
    }

    private static IEnumerable<string> WireFieldNames(Type dtoType) =>
        dtoType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => CamelCase.PropertyNamingPolicy!.ConvertName(p.Name));

    // Brace-counting extraction (regex alone can't safely match nested braces): finds "function <name>(" then
    // returns everything up to the matching closing brace.
    private static string ExtractFunctionBody(string source, string functionName)
    {
        string marker = "function " + functionName + "(";
        int start = source.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"'{marker}' should exist in the widget source");
        int braceOpen = source.IndexOf('{', start);
        int depth = 0;
        int i = braceOpen;
        for (; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    break;
            }
        }
        return source[braceOpen..(i + 1)];
    }
}
