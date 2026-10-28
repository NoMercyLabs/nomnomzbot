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

import type { ChatLine } from '@/api/chat';

// The words of a stored chat line: its message, or the pieces it was built from when the message is blank
// (an emote-only line stores no plain text). An empty string means the line shows nothing.
export function lineText(line: ChatLine): string {
  const message = line.message.trim();

  if (message !== '')
    return message;

  const pieces = (line.fragments ?? []).map((fragment) => fragment.text);

  return pieces.join(' ').trim();
}
