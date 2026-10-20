// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import type { RequiredOverlay } from '@/api/widgets';
import { i18n } from '@/i18n';

import overlaysEn from '../../overlays/i18n/en.json';
import RequiredOverlaysPanel from '../components/RequiredOverlaysPanel.vue';
import en from '../i18n/en.json';
import nl from '../i18n/nl.json';

const SECRET = 'SECRET123';
const BRIDGE_ADDRESS = `https://bot.example/obs-bridge?token=${SECRET}`;

const held = { configWrite: true, bridgeOpen: 1 };

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

vi.mock('@/composables/useCan', () => ({
  useCan: () => ({
    known: ref(true),
    can: (key: string): boolean => key !== 'obs:config:write' || held.configWrite,
  }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => ({
      data: {
        data: path.endsWith('/obs/bridge/setup')
          ? { bridgeUrl: BRIDGE_ADDRESS }
          : { instanceCount: held.bridgeOpen, hasLeader: held.bridgeOpen > 0 },
      },
      response: { status: 200, ok: true },
    }),
  },
}));

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
  held.configWrite = true;
  held.bridgeOpen = 1;
  i18n.global.mergeLocaleMessage('en', en);
  i18n.global.mergeLocaleMessage('en', overlaysEn);

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
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(RequiredOverlaysPanel, {
    attachTo: document.body,
    props: { overlays, panelState: 'ready' },
    global: {
      plugins: [
        i18n,
        [VueQueryPlugin, { queryClient }],
      ],
    },
  });
}

const rows = (): HTMLElement[] => Array.from(document.querySelectorAll('[data-required-overlay]'));
const buttons = (): HTMLButtonElement[] =>
  Array.from(document.querySelectorAll<HTMLButtonElement>('[data-obs-setup-open]'));

const bridgeButton = (): HTMLButtonElement | null =>
  buttons().at(-1) ?? null;

async function copyFromDialog(): Promise<void> {
  document.querySelector<HTMLButtonElement>('[data-obs-setup-copy]')!.click();
  await flushPromises();
}

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

  it('names the music player surface in both languages, never by its raw key', () => {
    open([surface('music_player')]);

    expect(rows()[0]?.textContent).toContain('Music Player');
    expect(rows()[0]?.textContent).not.toContain('dashboard.obs');
    expect(nl.dashboard.obs.required.surfaces.music_player.name).toBe('Muziekspeler');
  });

  it('copies the full address from the setup dialog and never prints the token on the page', async () => {
    open([surface('audio')]);

    buttons()[0]!.click();
    await flushPromises();
    await copyFromDialog();

    expect(written).toEqual([`https://bot.example/overlay?widgetId=w-audio&token=${SECRET}`]);
    expect(document.body.innerHTML).not.toContain(SECRET);
  });

  it('says Connect on a surface not loaded in OBS and Configure on one that is', () => {
    open([surface('audio', { isAttached: true }), surface('alerts')]);

    expect(buttons()[0]?.textContent).toContain('Configure');
    expect(buttons()[1]?.textContent).toContain('Connect');
  });

  it('teaches the catalog frame size and OBS boxes of a surface in its dialog', async () => {
    open([
      surface('tts_caption', {
        obsSetup: {
          width: 1920,
          height: 1080,
          controlAudioViaObs: false,
          shutdownWhenHidden: false,
        },
      }),
    ]);

    buttons()[0]!.click();
    await flushPromises();

    const facts = document.querySelector('[data-obs-setup-facts]')?.textContent ?? '';

    expect(facts).toContain('1920 x 1080, full screen');
    expect(facts).toContain('Control audio via OBS');
  });

  it('gives only the first surface that is not open in OBS the primary button', () => {
    open([
      surface('audio', { isAttached: true }),
      surface('alerts'),
      surface('tts_caption'),
      surface('raid_recap', { isEnabled: false }),
    ]);

    const primary = buttons().map((button) => button.className.includes('rounded-full'));

    expect(primary).toEqual([false, true, false, false, false]);
  });

  it('copies nothing for a surface that has no address yet', async () => {
    open([surface('audio', { overlayUrl: null })]);

    buttons()[0]!.click();
    await flushPromises();
    await copyFromDialog();

    expect(written).toEqual([]);
  });

  it('lists the OBS control bridge and copies its setup address without printing it', async () => {
    open([surface('audio')]);
    await flushPromises();

    const row = document.querySelector('[data-required-bridge]');

    expect(row?.textContent).toContain(en.dashboard.obs.required.bridge.name);
    expect(row?.textContent).toContain(en.dashboard.obs.sources.loaded);

    bridgeButton()?.click();
    await flushPromises();
    await copyFromDialog();

    expect(written).toEqual([BRIDGE_ADDRESS]);
    expect(document.body.innerHTML).not.toContain(SECRET);
  });

  it('shows the bridge as not loaded when no bridge page is open in OBS', async () => {
    held.bridgeOpen = 0;
    open([surface('audio')]);
    await flushPromises();

    expect(document.querySelector('[data-required-bridge]')?.textContent).toContain(
      en.dashboard.obs.sources.notLoaded,
    );
  });

  it('locks the bridge Copy button for a viewer who cannot change the OBS link', async () => {
    held.configWrite = false;
    open([surface('audio')]);
    await flushPromises();

    const row = document.querySelector('[data-required-bridge]');

    expect(bridgeButton()?.disabled).toBe(true);
    expect(row?.textContent).toContain(en.dashboard.obs.required.bridge.locked);
  });
});
