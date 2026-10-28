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

using System.Collections.Concurrent;

namespace NomNomzBot.Infrastructure.Identity.Builtins;

/// <summary>
/// The in-memory per-subject confirm token behind <c>!forgetme</c> (gdpr-crypto.md §9): the first call
/// arms a 60-second window for the subject; <c>!forgetme confirm</c> consumes it. Deliberately
/// process-local and unpersisted — an armed-but-never-confirmed request must simply evaporate, and a
/// restart forgetting pending confirmations is the SAFE direction for an irreversible crypto-shred.
/// Registered as a singleton (the scoped built-ins share one pending set across chat messages).
/// </summary>
public sealed class ErasureConfirmationTracker
{
    /// <summary>How long an armed confirmation stays valid (spec-fixed: 60s TTL).</summary>
    public static readonly TimeSpan ConfirmationWindow = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _pending = new();
    private readonly TimeProvider _clock;

    public ErasureConfirmationTracker(TimeProvider clock)
    {
        _clock = clock;
    }

    /// <summary>Arms (or re-arms) the subject's confirmation window.</summary>
    public void Begin(Guid subjectUserId)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        _pending[subjectUserId] = now + ConfirmationWindow;

        // Opportunistic sweep so subjects who arm and walk away never accumulate.
        foreach (KeyValuePair<Guid, DateTimeOffset> entry in _pending)
            if (entry.Value < now)
                _pending.TryRemove(entry.Key, out DateTimeOffset _);
    }

    /// <summary>
    /// Consumes the subject's pending confirmation. True only when one was armed and is still inside
    /// its window; the token is single-use either way (an expired one is removed, not honored).
    /// </summary>
    public bool TryConsume(Guid subjectUserId)
    {
        if (!_pending.TryRemove(subjectUserId, out DateTimeOffset expiresAt))
            return false;
        return expiresAt >= _clock.GetUtcNow();
    }
}
