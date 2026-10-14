// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
  it('shows the still frames, small and large, when the reader asked for less motion', async () => {
    prefers(true);
    const { catalogEmoteCard, emoteFrames, smallestUrl } = await load();

    expect(smallestUrl(emoteFrames(animated))).toBe('https://cdn.7tv.app/e1/1x_static.webp');
    expect(catalogEmoteCard(animated, t)?.image).toBe('https://cdn.7tv.app/e1/4x_static.webp');
  });

  it('keeps the moving frames when the reader did not ask for less motion', async () => {
    prefers(false);
    const { catalogEmoteCard, emoteFrames, smallestUrl } = await load();

    expect(smallestUrl(emoteFrames(animated))).toBe('https://cdn.7tv.app/e1/1x.webp');
    expect(catalogEmoteCard(animated, t)?.image).toBe('https://cdn.7tv.app/e1/4x.webp');
  });

  it('keeps the moving frames when the server sent no still ones', async () => {
    prefers(true);
    const { emoteFrames, smallestUrl } = await load();

    expect(smallestUrl(emoteFrames({ ...animated, staticUrls: null }))).toBe(
      'https://cdn.7tv.app/e1/1x.webp',
    );
  });
});
