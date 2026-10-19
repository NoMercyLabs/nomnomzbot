// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
