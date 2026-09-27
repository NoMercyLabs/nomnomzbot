// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Diagnostics;
using NomNomzBot.Application.Abstractions.Platform;

namespace NomNomzBot.Infrastructure.Platform.Diagnostics;

/// <summary>
/// Measures this process's CPU share between consecutive samples: the processor time it consumed over the
/// wall-clock time that passed, across every core. A singleton, because the previous sample is the
/// baseline; the operator hub's status publisher samples it on its heartbeat.
/// </summary>
public sealed class ProcessCpuSampler : IProcessCpuSampler
{
    private readonly TimeProvider _clock;
    private readonly Func<TimeSpan> _processorTime;
    private readonly int _processorCount;
    private readonly Lock _gate = new();
    private DateTimeOffset? _lastSampledAt;
    private TimeSpan _lastProcessorTime;

    public ProcessCpuSampler(TimeProvider clock)
        : this(
            clock,
            () => Process.GetCurrentProcess().TotalProcessorTime,
            Environment.ProcessorCount
        ) { }

    internal ProcessCpuSampler(TimeProvider clock, Func<TimeSpan> processorTime, int processorCount)
    {
        _clock = clock;
        _processorTime = processorTime;
        _processorCount = processorCount;
    }

    public double SamplePercent()
    {
        lock (_gate)
        {
            DateTimeOffset now = _clock.GetUtcNow();
            TimeSpan cpu = _processorTime();
            double percent = _lastSampledAt is { } last
                ? Percent(cpu - _lastProcessorTime, now - last, _processorCount)
                : 0;
            _lastSampledAt = now;
            _lastProcessorTime = cpu;
            return percent;
        }
    }

    /// <summary>Processor time over wall time, spread across the cores, clamped to a real percentage.</summary>
    internal static double Percent(TimeSpan cpuDelta, TimeSpan wallDelta, int processorCount)
    {
        if (wallDelta <= TimeSpan.Zero || processorCount <= 0)
            return 0;
        double share = cpuDelta.TotalMilliseconds / (wallDelta.TotalMilliseconds * processorCount);
        return Math.Round(Math.Clamp(share * 100, 0, 100), 1);
    }
}
