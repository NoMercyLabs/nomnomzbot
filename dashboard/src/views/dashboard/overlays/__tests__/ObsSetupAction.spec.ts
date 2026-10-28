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

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import type { ObsBrowserSource } from '@/api/widgets';
import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import ObsSetupAction from '../components/ObsSetupAction.vue';

// Whether the bot controls OBS and so reloads a stranded source itself; each test sets it.
const botRepairs = ref<boolean>(false);

vi.mock('@/composables/useObsRepairsWidgets', () => ({
  useObsRepairsWidgets: () => botRepairs,
}));

const SECRET = 'SECRET123';
const ADDRESS = `https://bot.example/overlay?token=${SECRET}`;
const FULL = {
  width: 1920,
  height: 1080,
  controlAudioViaObs: false,
  shutdownWhenHidden: false,
};
const SOUND = {
  width: 1,
  height: 1,
  controlAudioViaObs: true,
  shutdownWhenHidden: false,
};

const written: string[] = [];
let mounted: VueWrapper | null = null;

beforeEach(() => {
  written.length = 0;
  botRepairs.value = false;
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

function show(
  setup: ObsBrowserSource | null,
  connected: boolean,
  primary = false,
  expected = false,
): void {
  mounted = mount(ObsSetupAction, {
    attachTo: document.body,
    props: {
      title: 'TTS caption',
      setup,
      connected,
      primary,
      expected,
      resolveAddress: async () => ADDRESS,
    },
    global: { plugins: [i18n] },
  });
}

const opener = (): HTMLButtonElement =>
  document.querySelector<HTMLButtonElement>('[data-obs-setup-open]')!;

async function openDialog(): Promise<void> {
  opener().click();
  await flushPromises();
}

const fact = (id: string): string =>
  document.querySelector(`[data-obs-setup-fact="${id}"]`)?.textContent ?? '';

describe('ObsSetupAction', () => {
  it('says Connect while the overlay is not loaded in OBS and Configure once it is', () => {
    show(FULL, false);
    expect(opener().textContent).toContain(en.dashboard.overlays.setup.connect);
    mounted?.unmount();

    show(FULL, true);
    expect(opener().textContent).toContain(en.dashboard.overlays.setup.configure);
  });

  it('makes Connect the primary button only when told it is next, and Configure never', () => {
    show(FULL, false, true);
    expect(opener().className).toContain('rounded-full');
    mounted?.unmount();

    show(FULL, false, false);
    expect(opener().className).not.toContain('rounded-full');
    mounted?.unmount();

    show(FULL, true, true);
    expect(opener().className).not.toContain('rounded-full');
  });

  it('teaches the frame size and both OBS boxes from the catalog values', async () => {
    show(FULL, false);
    await openDialog();

    expect(fact('size')).toContain('1920 x 1080, full screen');
    expect(fact('audio')).toContain(en.dashboard.overlays.setup.audio.off);
    expect(fact('shutdown')).toContain(en.dashboard.overlays.setup.shutdown.off);
  });

  it('tells a sound source to tick audio control and says a 1 x 1 source draws nothing', async () => {
    show(SOUND, false);
    await openDialog();

    expect(fact('size')).toContain('1 x 1, it draws nothing');
    expect(fact('audio')).toContain(en.dashboard.overlays.setup.audio.on);
  });

  it('warns loudly when an overlay that is switched on has no connection and the bot cannot repair it', () => {
    show(FULL, false, false, true);
    const warning = document.querySelector('[data-obs-setup-warning]');

    expect(warning?.getAttribute('role')).toBe('alert');
    expect(warning?.textContent).toContain(en.dashboard.overlays.setup.stranded);
    expect(warning?.getAttribute('title')).toBe(en.dashboard.overlays.setup.strandedHint);
  });

  it('stays quiet when the bot controls OBS and reloads the source itself', () => {
    botRepairs.value = true;
    show(FULL, false, false, true);

    expect(document.querySelector('[data-obs-setup-warning]')).toBeNull();
  });

  it('stays quiet for a connected overlay and for one that is not expected in OBS', () => {
    show(FULL, true, false, true);
    expect(document.querySelector('[data-obs-setup-warning]')).toBeNull();
    mounted?.unmount();

    show(FULL, false, false, false);
    expect(document.querySelector('[data-obs-setup-warning]')).toBeNull();
  });

  it('shows the live status in the dialog', async () => {
    show(FULL, true);
    await openDialog();

    expect(document.querySelector('[data-obs-setup-status]')?.textContent).toContain(
      en.dashboard.overlays.setup.loaded,
    );
  });

  it('shows no facts for a widget without catalog values but still gives the steps', async () => {
    show(null, false);
    await openDialog();

    expect(document.querySelector('[data-obs-setup-facts]')).toBeNull();
    expect(document.body.textContent).toContain(en.dashboard.overlays.setup.steps.add);
  });

  it('copies the address only from the dialog and never prints the token', async () => {
    show(FULL, false);
    expect(document.querySelector('[data-obs-setup-copy]')).toBeNull();

    await openDialog();
    document.querySelector<HTMLButtonElement>('[data-obs-setup-copy]')!.click();
    await flushPromises();

    expect(written).toEqual([ADDRESS]);
    expect(document.body.innerHTML).not.toContain(SECRET);
  });
});
