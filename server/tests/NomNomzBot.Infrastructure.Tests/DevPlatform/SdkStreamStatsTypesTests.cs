// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Dashboard.Stats;
using NomNomzBot.Application.DevPlatform;
using NomNomzBot.Infrastructure.DevPlatform;
using NomNomzBot.Infrastructure.Platform;

namespace NomNomzBot.Infrastructure.Tests.DevPlatform;

/// <summary>
/// <c>streamStats</c> is typed from the metric registry: every <see cref="IStreamMetric"/> the DI scan registers
/// is a key of <c>NnzStreamMetricMap</c> in both SDK contexts, so a new metric class reaches the editor typings
/// without a hand edit.
/// </summary>
public sealed class SdkStreamStatsTypesTests
{
    private static List<string> RegisteredMetricKeys()
    {
        ServiceCollection services = new();
        services.AddImplementationsOf<IStreamMetric>(
            typeof(StreamMetricTypeScript).Assembly,
            ServiceLifetime.Scoped
        );
        return services
            .Select(d =>
                ((IStreamMetric)RuntimeHelpers.GetUninitializedObject(d.ImplementationType!)).Key
            )
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static string MetricMapBlock(string typeScript)
    {
        int start = typeScript.IndexOf("interface NnzStreamMetricMap {", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the metric map is part of the generated d.ts");
        int end = typeScript.IndexOf('}', start);
        return typeScript[start..end];
    }

    [Theory]
    [InlineData(SdkContext.Widget)]
    [InlineData(SdkContext.Script)]
    public void Every_registered_metric_key_is_a_key_of_the_metric_map(SdkContext context)
    {
        List<string> keys = RegisteredMetricKeys();
        keys.Should().HaveCountGreaterThan(10, "the registry holds the whole recap metric set");

        string block = MetricMapBlock(
            new SdkTypeEmitter(new EventCatalog()).EmitTypeScript(context)
        );

        foreach (string key in keys)
            block.Should().MatchRegex($@"\n  {key}\??: ", $"metric '{key}' is registered");
        int declared = block
            .Split('\n')
            .Count(l =>
                l.StartsWith("  ", StringComparison.Ordinal)
                && !l.StartsWith("  /**", StringComparison.Ordinal)
            );
        declared.Should().Be(keys.Count, "the map holds exactly the registered metrics");
    }

    [Fact]
    public void A_metric_that_can_have_nothing_to_show_is_an_optional_key_and_the_call_is_typed_by_Pick()
    {
        string ts = new SdkTypeEmitter(new EventCatalog()).EmitTypeScript(SdkContext.Widget);

        MetricMapBlock(ts).Should().Contain("peakViewers?: number;");
        MetricMapBlock(ts).Should().Contain("duration: number;");
        ts.Should()
            .Contain(
                "type NnzStreamStats<K extends NnzStreamMetricKey> = Pick<NnzStreamMetricMap, K> & { stream: NnzStreamWindow };"
            );
    }
}
