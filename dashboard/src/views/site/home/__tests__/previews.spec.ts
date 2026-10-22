// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { ownerActionKeys } from '../preview/replies/actionKeys';
import { startFakeApi, unmatched } from '../preview/fakeApi';
import { createPreviewApp, type PreviewApp } from '../preview/previewApp';
import {
  areaPages,
  groupPages,
  previewAreas,
  type PreviewArea,
} from '../preview/previewAreas';
import { sampleReplies } from '../preview/sampleReplies';
import { sampleChannel } from '../sampleChannel';

// The text each real page shows once the sample has filled it. It comes from the sample records, so a page that
// stopped reading them, or a record that stopped matching, fails here.
const shown = {
  pipelines: 'Raid response',
  moderation: 'Spam defense',
  widgets: 'Goal Bar',
  analytics: 'Peak viewers',
  commands: 'compliment',
  loyalty: 'Chat messages',
  music: 'Paper Lanterns',
  platforms: 'Connected services',
} as const satisfies Record<PreviewArea, string>;

// jsdom draws nothing, so it has no observers; the pages only need them to exist.
class Watcher {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}

// The group view of a page can show another tab than the hero does.
const shownInGroup = {
  ...shown,
  pipelines: 'Big raid welcome',
  widgets: 'Now Playing',
  commands: 'welcome',
  analytics: 'Busy days and hours',
  loyalty: 'Hydrate!',
  moderation: 'Escalation ladder',
  music: 'good luck on the boss',
  platforms: 'Your channel\'s own bot',
} as const satisfies Record<PreviewArea, string>;

const fetched = vi.fn<(input: unknown) => Promise<Response>>(async () => Response.json({}));
let stop: (() => void) | null = null;
let preview: PreviewApp | null = null;
let host: HTMLElement | null = null;

beforeEach(() => {
  fetched.mockClear();
  vi.stubGlobal('fetch', fetched);
  vi.stubGlobal('ResizeObserver', Watcher);
  vi.stubGlobal('IntersectionObserver', Watcher);
  unmatched.length = 0;
  stop = startFakeApi();
  host = document.createElement('div');
  document.body.append(host);
});

afterEach(() => {
  preview?.app.unmount();
  preview = null;
  stop?.();
  stop = null;
  host?.remove();
  vi.unstubAllGlobals();
});

async function open(path: string, shell: boolean): Promise<PreviewApp> {
  preview = createPreviewApp(path, shell);
  preview.app.mount(host as HTMLElement);
  await preview.router.isReady();

  return preview;
}

// A page paints its title before its data answers; a read that failed shows its error line a moment later.
async function settle(): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 400));
}

describe('the sample data', () => {
  it('marks itself as sample and names a made-up channel', () => {
    expect(sampleChannel.isSample).toBe(true);
    expect(sampleChannel.handle).toBe('crumb_tv');
  });

  it('answers every request from the table, with no real channel id in any path', () => {
    expect(sampleReplies.length).toBeGreaterThan(30);

    for (const reply of sampleReplies) {
      expect(reply.path).not.toMatch(/\/channels\/[0-9]+/);
    }
  });
});

describe('the feature groups', () => {
  it.each(previewAreas)('show another real screen for %s than the hero tab shows', (area) => {
    expect(groupPages[area].path).not.toBe(areaPages[area].path);
  });

  it('give the commands hero room for the reply, the cooldowns and the action bar', () => {
    expect(areaPages.commands.height).toBeGreaterThanOrEqual(760);
  });
});

describe.each(previewAreas)('the %s preview', (area) => {
  it('mounts the real dashboard page, fills it from the sample, and asks the network for nothing', async () => {
    const { router } = await open(areaPages[area].path, true);

    await vi.waitFor(() => expect(host!.textContent).toContain(shown[area]), { timeout: 8000 });

    // The route is the dashboard's own route for the page, not a copy of it.
    await settle();

    expect(host!.textContent).not.toMatch(/could not be (loaded|saved)/);
    expect(router.currentRoute.value.matched.length).toBeGreaterThan(1);
    expect(router.currentRoute.value.fullPath).toBe(areaPages[area].path);
    expect(unmatched).toEqual([]);
    expect(fetched).not.toHaveBeenCalled();
  }, 15_000);

  it('shows the group view of the page without the sidebar', async () => {
    const { router } = await open(groupPages[area].path, false);

    await vi.waitFor(() => expect(host!.textContent).toContain(shownInGroup[area]), {
      timeout: 8000,
    });

    // A page that fell back to its load-error line is not a real screen of the product.
    await settle();

    expect(host!.textContent).not.toMatch(/could not be (loaded|saved)/);
    expect(router.currentRoute.value.fullPath).toBe(groupPages[area].path);
    expect(host!.querySelector('[data-sidebar="sidebar"], [data-slot="sidebar"]')).toBeNull();
    expect(unmatched).toEqual([]);
    expect(fetched).not.toHaveBeenCalled();
  }, 15_000);
});

describe('the owner of the sample channel', () => {
  it('holds an action key for every key the dashboard pages ask for', () => {
    const asked = import.meta.glob<string>('/src/views/dashboard/**/*.vue', {
      query: '?raw',
      import: 'default',
      eager: true,
    });

    const keys = new Set<string>();
    const pattern = /(?:can\(|action=|read: |create: |write: |manage: |action: )'([a-z][a-z0-9-]*(?::[a-z0-9-]+)+)'/g;

    for (const source of Object.values(asked)) {
      for (const match of source.matchAll(pattern)) {
        keys.add(match[1] as string);
      }
    }

    const held = new Set<string>(ownerActionKeys);
    const operatorOnly = /^(admin|platform|tenant|billing|compliance|eventstore|featureflag|iam|network|trust-safety|user|dashboard:replay)/;
    const missing = [...keys].filter((key) => !held.has(key) && !operatorOnly.test(key));

    expect(keys.size).toBeGreaterThan(80);
    expect(missing).toEqual([]);
  });

  it('writes every template in the sample with single braces, each name one the bot knows', () => {
    const commands = sampleReplies.find((reply) => reply.path.endsWith('/commands'));
    const helpers = sampleReplies.find((reply) => reply.path === '/api/v1/templates/helpers');
    const url = new URL('http://sample.test/');
    const text = JSON.stringify(commands?.body(url));
    const known = JSON.stringify(helpers?.body(url));
    const used = [...text.matchAll(/\{([^{}"]+)\}/g)].map((match) => match[1] as string);

    expect(text).not.toContain('{{');
    expect(used.length).toBeGreaterThan(3);

    const unknown = used.filter((name) => {
      const base = name.replace(/\.\d+$/, '.<n>').replace(/^count\..+$/, 'count.<key>');

      return !known.includes(`"${base}"`);
    });

    expect(unknown).toEqual([]);
  });
});
