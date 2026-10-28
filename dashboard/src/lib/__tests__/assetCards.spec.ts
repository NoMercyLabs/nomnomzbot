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

import { afterEach, describe, expect, it, vi } from 'vitest';

import type { ChatEmoteEntry } from '@/api/chat';

const animated = {
  id: 'e1',
  code: 'catJAM',
  provider: 'SevenTv',
  animated: true,
  zeroWidth: false,
  urls: { 1: 'https://cdn.7tv.app/e1/1x.webp', 4: 'https://cdn.7tv.app/e1/4x.webp' },
  staticUrls: {
    1: 'https://cdn.7tv.app/e1/1x_static.webp',
    4: 'https://cdn.7tv.app/e1/4x_static.webp',
  },
} as unknown as ChatEmoteEntry;

const t = (key: string): string =>
  key;

// The reader's system setting, as the browser would answer the media query.
function prefers(reduce: boolean): void {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: reduce && query.includes('reduce'),
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
  }));
}

// The motion flag is app-wide state, so each case loads the module fresh under its own setting.
async function load(): Promise<typeof import('@/lib/assetCards')> {
  vi.resetModules();

  return import('@/lib/assetCards');
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('emote frames', () => {
  // Owner 2026-10-14: "bring back the moving emotes". Emotes are chat content, like on Twitch, so the
  // system's less-motion setting does not freeze them.
  it('keeps the moving frames, small and large, even when the system asks for less motion', async () => {
    prefers(true);
    const { catalogEmoteCard, emoteFrames, smallestUrl } = await load();

    expect(smallestUrl(emoteFrames(animated))).toBe('https://cdn.7tv.app/e1/1x.webp');
    expect(catalogEmoteCard(animated, t)?.image).toBe('https://cdn.7tv.app/e1/4x.webp');
  });
});
