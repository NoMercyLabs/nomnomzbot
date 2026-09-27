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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Infrastructure.Platform.Diagnostics;

namespace NomNomzBot.Infrastructure.Tests.Platform.Diagnostics;

public sealed class ProcessCpuSamplerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_first_sample_has_no_baseline_and_the_next_one_measures_the_interval_since_it()
    {
        FakeTimeProvider clock = new(Start);
        TimeSpan processorTime = TimeSpan.FromSeconds(10);
        ProcessCpuSampler sampler = new(clock, () => processorTime, processorCount: 2);

        sampler.SamplePercent().Should().Be(0, "nothing came before the first sample");

        // One wall second passes; the process burned half a second of CPU across two cores → 25 %.
        clock.Advance(TimeSpan.FromSeconds(1));
        processorTime += TimeSpan.FromMilliseconds(500);
        sampler.SamplePercent().Should().Be(25);

        // The next interval is measured from the previous sample, not from the start.
        clock.Advance(TimeSpan.FromSeconds(2));
        processorTime += TimeSpan.FromSeconds(4);
        sampler.SamplePercent().Should().Be(100, "both cores were busy the whole two seconds");
    }

    [Theory]
    [InlineData(500, 1000, 1, 50)]
    [InlineData(500, 1000, 4, 12.5)]
    [InlineData(5000, 1000, 2, 100)]
    [InlineData(500, 0, 2, 0)]
    public void Percent_is_processor_time_over_wall_time_across_the_cores_clamped_to_a_real_percentage(
        int cpuMs,
        int wallMs,
        int cores,
        double expected
    )
    {
        ProcessCpuSampler
            .Percent(TimeSpan.FromMilliseconds(cpuMs), TimeSpan.FromMilliseconds(wallMs), cores)
            .Should()
            .Be(expected);
    }
}
