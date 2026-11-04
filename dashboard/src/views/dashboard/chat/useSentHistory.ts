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

export const sentHistoryLimit = 50;

// The messages this box sent, as BTTV and FFZ keep them: Up walks back to older ones, Down walks forward
// and ends on the draft that was in the box when the walk began. A send starts the walk over.
export function useSentHistory() {
  const sent: string[] = [];
  let at = -1;
  let draft = '';

  function record(message: string): void {
    if (sent.at(-1) !== message) {
      sent.push(message);
    }

    if (sent.length > sentHistoryLimit) {
      sent.shift();
    }

    at = -1;
  }

  function older(current: string): string | null {
    if (at === -1) {
      if (sent.length === 0)
        return null;

      draft = current;
      at = sent.length;
    }

    if (at === 0)
      return null;

    at -= 1;

    return sent[at]!;
  }

  function newer(): string | null {
    if (at === -1)
      return null;

    at += 1;

    if (at < sent.length)
      return sent[at]!;

    at = -1;

    return draft;
  }

  return {
    record,
    older,
    newer,
  };
}
