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

import type { JsonValue } from '@/lib/json';

export type TtsBlock = Record<string, JsonValue>;

export function readTtsBlocks(value: JsonValue | undefined): TtsBlock[] | null {
  if (value === undefined || value === '')
    return [];

  try {
    const parsed: JsonValue = typeof value === 'string'
      ? JSON.parse(value)
      : value;

    if (!Array.isArray(parsed) || !parsed.every((block) =>
      typeof block === 'object' && block !== null && !Array.isArray(block)))
      return null;

    return parsed as TtsBlock[];
  } catch {
    return null;
  }
}

export function setTtsBlockField(
  blocks: TtsBlock[],
  index: number,
  key: string,
  value: JsonValue | undefined,
): string {
  const next = blocks.map((block, at) =>
    at === index
      ? { ...block }
      : block);
  const block = next[index];

  if (!block)
    return JSON.stringify(next);

  if (value === undefined || value === '') {
    delete block[key];
  } else {
    block[key] = value;
  }

  return JSON.stringify(next);
}
