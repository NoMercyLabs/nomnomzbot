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

    public static TheoryData<Row> Rows =>
        new()
        {
            new Row("recent_followers", "onFollow", typeof(FollowAlertDto)),
            new Row("top_cheerers", "onCheer", typeof(CheerAlertDto)),
            new Row("sub_train", "onGift", typeof(GiftSubAlertDto)),
            new Row("labels", "onFollow", typeof(FollowAlertDto)),
            new Row("labels", "onSub", typeof(SubscriptionAlertDto)),
            new Row("labels", "onResub", typeof(ResubAlertDto)),
            new Row("labels", "onGift", typeof(GiftSubAlertDto)),
            new Row("labels", "onCheer", typeof(CheerAlertDto)),
            new Row("goal_bar", "onGift", typeof(GiftSubAlertDto)),
            new Row("goal_bar", "onCheer", typeof(CheerAlertDto)),
            new Row(
                "alerts",
                "nameOf",
                [
                    typeof(FollowAlertDto),
                    typeof(SubscriptionAlertDto),
                    typeof(ResubAlertDto),
                    typeof(GiftSubAlertDto),
                    typeof(CheerAlertDto),
                    typeof(RaidAlertDto),
                ],
                // Deliberate legacy-compatibility fallback (nameOf: `d.displayName || d.user`) — not a mismatch.
                ["user"]
            ),
            new Row(
                "alerts",
                "cardFor",
                [
                    typeof(FollowAlertDto),
                    typeof(SubscriptionAlertDto),
                    typeof(ResubAlertDto),
                    typeof(GiftSubAlertDto),
                    typeof(CheerAlertDto),
                    typeof(RaidAlertDto),
                ],
                // supporter.tip/membership/merch/charity have no first-party DTO yet (out of scope for this
                // fix — see the task's widget field-alignment scope); `message`/`amount`/`currency` are read
                // only on those branches.
                ["message", "amount", "currency"]
            ),
            new Row(
                "event_ticker",
                "nameOf",
                [
                    typeof(FollowAlertDto),
                    typeof(SubscriptionAlertDto),
                    typeof(ResubAlertDto),
                    typeof(GiftSubAlertDto),
                    typeof(CheerAlertDto),
                    typeof(RaidAlertDto),
                ],
                ["user"]
            ),
            new Row(
                "event_ticker",
                "chipFor",
                [
                    typeof(FollowAlertDto),
                    typeof(SubscriptionAlertDto),
                    typeof(ResubAlertDto),
                    typeof(GiftSubAlertDto),
                    typeof(CheerAlertDto),
                    typeof(RaidAlertDto),
                ],
                ["message", "amount", "currency"]
            ),
        };

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
