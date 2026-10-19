// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import type { RequiredOverlay } from '@/api/widgets';
import { i18n } from '@/i18n';

import RequiredOverlaysPanel from '../components/RequiredOverlaysPanel.vue';
import en from '../i18n/en.json';
import nl from '../i18n/nl.json';

const SECRET = 'SECRET123';

const surface = (key: string, over: Record<string, unknown> = {}): RequiredOverlay =>
  ({
    key,
    widget: {
      id: `w-${key}`,
      name: key,
      isEnabled: true,
      isAttached: false,
      lastRuntimeError: null,
      overlayUrl: `https://bot.example/overlay?widgetId=w-${key}&token=${SECRET}`,
      ...over,
    },
  }) as unknown as RequiredOverlay;

const written: string[] = [];
let mounted: VueWrapper | null = null;

beforeEach(() => {
  written.length = 0;
  i18n.global.mergeLocaleMessage('en', en);

  Object.defineProperty(navigator, 'clipboard', {
    configurable: true,
    value: {
      writeText: async (text: string): Promise<void> => {
        written.push(text);
      },
    },
  });
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

function open(overlays: RequiredOverlay[]): void {
  mounted = mount(RequiredOverlaysPanel, {
    attachTo: document.body,
    props: { overlays, panelState: 'ready' },
    global: { plugins: [i18n] },
  });
}

const rows = (): HTMLElement[] => Array.from(document.querySelectorAll('[data-required-overlay]'));
const buttons = (): HTMLButtonElement[] =>
  Array.from(document.querySelectorAll<HTMLButtonElement>('[data-required-copy]'));

describe('RequiredOverlaysPanel', () => {
  it('lists every system surface by name under the #required-overlays anchor', () => {
    open([surface('audio'), surface('alerts'), surface('tts_caption'), surface('raid_recap')]);

    expect(document.querySelector('#required-overlays')).not.toBeNull();

    expect(rows().map((row) => row.getAttribute('data-required-overlay'))).toEqual([
      'audio',
      'alerts',
      'tts_caption',
      'raid_recap',
    ]);

    expect(rows()[1]?.textContent).toContain(en.dashboard.obs.required.surfaces.alerts.name);
    expect(rows()[1]?.textContent).toContain(en.dashboard.obs.required.surfaces.alerts.detail);
  });

  it('names the audio surface Bot Audio and leaves songs to the music player', () => {
    open([surface('audio')]);

    expect(rows()[0]?.textContent).toContain('Bot Audio');
    expect(rows()[0]?.textContent).toContain(en.dashboard.obs.required.surfaces.audio.detail);
    expect(en.dashboard.obs.required.surfaces.audio.detail).not.toMatch(/song/i);
    expect(nl.dashboard.obs.required.surfaces.audio.name).toBe('Bot Audio');
  });

  it('copies the full address to the clipboard and never prints the token on the page', async () => {
    open([surface('audio')]);

    buttons()[0]!.click();
    await flushPromises();

    expect(written).toEqual([`https://bot.example/overlay?widgetId=w-audio&token=${SECRET}`]);
    expect(buttons()[0]?.textContent).toContain(en.dashboard.obs.required.copied);
    expect(document.body.innerHTML).not.toContain(SECRET);
  });

  it('gives only the first surface that is not open in OBS the primary button', () => {
    open([
      surface('audio', { isAttached: true }),
      surface('alerts'),
      surface('tts_caption'),
      surface('raid_recap', { isEnabled: false }),
    ]);

    const primary = buttons().map((button) => button.className.includes('rounded-full'));

    expect(primary).toEqual([false, true, false, false]);
  });

  it('disables Copy for a surface that has no address yet', () => {
    open([surface('audio', { overlayUrl: null })]);

    expect(buttons()[0]?.disabled).toBe(true);
  });
});
