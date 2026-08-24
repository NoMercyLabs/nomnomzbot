// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Infrastructure.Webhooks.Adapters;

namespace NomNomzBot.Infrastructure.Tests.Webhooks;

/// <summary>
/// Proves <see cref="WebhookAdapterHelpers.FlattenJson"/> bounds its recursion (S041/F16): a payload nested
/// far deeper than the cap must flatten without throwing (no unbounded stack growth) and the resulting bag
/// must be truncated at the cap depth rather than descending arbitrarily far.
/// </summary>
public sealed class WebhookFlattenDepthCapTests
{
    [Fact]
    public void FlattenJson_bounds_a_shallow_payload_at_full_depth()
    {
        string json = """{"a":{"b":{"c":"leaf"}}}""";

        Dictionary<string, string> result = WebhookAdapterHelpers.FlattenJson(json);

        result.Should().ContainKey("a.b.c").WhoseValue.Should().Be("leaf");
    }

    [Fact]
    public void FlattenJson_truncates_a_deeply_nested_payload_instead_of_recursing_unbounded()
    {
        // Kept below Newtonsoft's own JsonReader.MaxDepth (default 64) so the payload parses at all; the point
        // here is proving OUR flatten cap (32) truncates before recursing all the way to the parsed leaf.
        const int nestingLevels = 50;
        System.Text.StringBuilder json = new();
        for (int i = 0; i < nestingLevels; i++)
            json.Append($$"""{"n{{i}}":""");
        json.Append("\"leaf\"");
        for (int i = 0; i < nestingLevels; i++)
            json.Append('}');

        Action act = () => WebhookAdapterHelpers.FlattenJson(json.ToString());

        act.Should().NotThrow<StackOverflowException>();

        Dictionary<string, string> result = WebhookAdapterHelpers.FlattenJson(json.ToString());
        result.Should().NotBeEmpty();

        // Every recorded key must be bounded at the cap: no key can carry more than the cap's worth of
        // dot-separated segments, proving deeper structure was truncated rather than flattened forever.
        int maxSegments = result.Keys.Max(k => k.Split('.').Length);
        maxSegments.Should().BeLessThanOrEqualTo(32);

        // The truncated branch is recorded as its remaining nested JSON, not silently dropped.
        result.Values.Should().Contain(v => v.Contains("leaf"));
    }
}
