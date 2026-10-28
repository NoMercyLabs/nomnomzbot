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

using NomNomzBot.Application.Contracts.Security;

namespace NomNomzBot.Infrastructure.Platform.Security;

/// <summary>
/// <see cref="AsyncLocal{T}"/>-backed sanction scope. Flows with the async call chain, so a sanction opened at
/// the entry point covers everything that entry point goes on to do, including work handed to another service.
/// It does NOT flow to a fire-and-forget task started outside the scope, which is the correct default: work the
/// bot decides to do on its own schedule has to name its own basis rather than inherit somebody's.
/// </summary>
public sealed class OutboundSanctionAccessor : IOutboundSanctionAccessor
{
    private static readonly AsyncLocal<OutboundSanction?> Ambient = new();

    public OutboundSanction? Current => Ambient.Value;

    public IDisposable Begin(OutboundSanction sanction)
    {
        OutboundSanction? previous = Ambient.Value;
        Ambient.Value = sanction;
        return new Scope(previous);
    }

    private sealed class Scope(OutboundSanction? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Ambient.Value = previous;
        }
    }
}
