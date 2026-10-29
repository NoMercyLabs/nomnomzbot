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

using System.Diagnostics;

namespace NomNomzBot.Infrastructure.CustomCode.Jint;

/// <summary>
/// The clock of one script run. It splits the wall time into the script's own run time (statements, bootstrap,
/// nnz.time.sleep) and the time spent waiting inside host calls. Only the script's own time counts against the
/// run budget: <see cref="Token"/> is cancelled when that budget is spent, and the timer is paused for as long
/// as a host call runs (<see cref="WaitOnHost{T}"/>). The external token still cancels the run at any time.
/// </summary>
internal sealed class ScriptRunClock : IDisposable
{
    private readonly int _budgetMs;
    private readonly CancellationTokenSource _budget;
    private readonly Stopwatch _wall = Stopwatch.StartNew();
    private TimeSpan _hostWait = TimeSpan.Zero;

    public ScriptRunClock(int budgetMs, CancellationToken external)
    {
        _budgetMs = budgetMs;
        _budget = CancellationTokenSource.CreateLinkedTokenSource(external);
        _budget.CancelAfter(TimeSpan.FromMilliseconds(budgetMs));
    }

    /// <summary>Cancelled when the script's own run time passes the budget, or when the caller cancels.</summary>
    public CancellationToken Token => _budget.Token;

    /// <summary>The script's own run time in ms: the wall time minus the host-call wait.</summary>
    public long ScriptMs => (long)(_wall.Elapsed - _hostWait).TotalMilliseconds;

    /// <summary>The time in ms the run spent waiting inside host calls.</summary>
    public long HostWaitMs => (long)_hostWait.TotalMilliseconds;

    /// <summary>
    /// Runs one host call with the run budget paused, then starts it again with the time that is left. A run
    /// whose budget is already spent starts no new host call.
    /// </summary>
    public T WaitOnHost<T>(Func<T> call)
    {
        _budget.Token.ThrowIfCancellationRequested();
        _budget.CancelAfter(Timeout.InfiniteTimeSpan);
        long started = Stopwatch.GetTimestamp();
        try
        {
            return call();
        }
        finally
        {
            _hostWait += Stopwatch.GetElapsedTime(started);
            long remainingMs = _budgetMs - ScriptMs;
            if (remainingMs <= 0)
                _budget.Cancel();
            else
                _budget.CancelAfter(TimeSpan.FromMilliseconds(remainingMs));
        }
    }

    /// <summary>Freezes the clock at the end of the run, so the numbers read afterwards do not grow.</summary>
    public void Stop() => _wall.Stop();

    public void Dispose() => _budget.Dispose();
}
