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

namespace NomNomzBot.Application.Music.Services;

/// <summary>
/// The one numbering of the song queue that viewers see. <c>!queue</c> prints entry number N for the entry at
/// zero-based position N-1, and <c>!wrongsong N</c> reads the same number back — both go through here so the
/// number a viewer is shown and the number they can type never drift apart.
/// </summary>
public static class QueueNumbering
{
    /// <summary>The number shown for the entry at the zero-based <paramref name="position"/>.</summary>
    public static int NumberOf(int position) => position + 1;

    /// <summary>The zero-based position a typed <paramref name="number"/> names, or <c>null</c> when it is outside the queue.</summary>
    public static int? PositionOf(int number, int queueLength) =>
        number >= 1 && number <= queueLength ? number - 1 : null;
}
