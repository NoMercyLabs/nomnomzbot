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
import { afterEach, beforeAll, describe, expect, it } from 'vitest';

import { i18n } from '@/i18n';
import type { JsonObject } from '@/lib/json';

import SettingGroup from '../components/SettingGroup.vue';
import en from '../i18n/en.json';

beforeAll(() => {
  i18n.global.mergeLocaleMessage('en', en);
});

const eras = (): JsonObject => ({
  win31: { enabled: true, timings: { glitch: 3000, bsod: 16000 } },
  win95: { enabled: false, timings: { glitch: 3000, bsod: 15000 } },
});

let mounted: VueWrapper | null = null;

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

function mountGroup(value: JsonObject): VueWrapper {
  mounted = mount(SettingGroup, {
    attachTo: document.body,
    props: { value },
    global: { plugins: [i18n] },
  });

  return mounted;
}

async function openEra(group: VueWrapper, key: string): Promise<void> {
  await group.get(`[data-group-key="${key}"] [data-group-toggle]`).trigger('click');
  await flushPromises();
}

describe('a nested settings group', () => {
  it('names each part in plain words, never by its code name', () => {
    const group = mountGroup(eras());

    expect(group.text()).toContain('Windows 3.1');
    expect(group.text()).toContain('Windows 95');
    expect(group.text()).not.toContain('Win31');
    expect(group.text()).not.toContain('Win95');
  });

  it('reads a compound key in plain words', () => {
    const group = mountGroup({ winXpStartupSoundUrl: 'https://cdn.example/xp.mp3' });

    expect(group.text()).toContain('Windows XP startup sound link');
    expect(group.text()).not.toContain('Url');
  });

  it('keeps each nested part closed until it is opened, so the list fits one screen', async () => {
    const group = mountGroup(eras());

    expect(group.find('[data-group-key="glitch"]').exists()).toBe(false);

    await openEra(group, 'win31');

    expect(group.find('[data-group-key="win31"] [data-group-key="glitch"]').exists()).toBe(true);
    expect(group.find('[data-group-key="win95"] [data-group-key="glitch"]').exists()).toBe(false);
  });

  it('keeps the on switch of a nested part on its closed row, so the list stays short', async () => {
    const group = mountGroup(eras());
    const head = group.get('[data-group-key="win31"] [data-group-head]');

    expect(head.find('[role="switch"]').exists()).toBe(true);
    expect(head.text()).toContain('Windows 3.1');

    await openEra(group, 'win31');

    expect(group.findAll('[data-group-key="win31"] [role="switch"]')).toHaveLength(1);
  });

  it('shows timings in seconds with their unit and saves them back as milliseconds', async () => {
    const group = mountGroup(eras());

    await openEra(group, 'win31');

    const glitch = group.get('[data-group-key="win31"] [data-group-key="glitch"]');
    const box = glitch.get('input').element as HTMLInputElement;

    expect(box.value).toBe('3');
    expect(glitch.text()).toContain('Glitch');
    expect(glitch.text()).toContain('s');
    expect(group.get('[data-group-key="win31"] [data-group-key="bsod"]').text()).toContain('Blue screen');

    box.value = '2.5';
    box.dispatchEvent(new Event('input', { bubbles: true }));
    await flushPromises();

    expect(group.emitted('update')?.at(-1)).toEqual([
      {
        ...eras(),
        win31: { enabled: true, timings: { glitch: 2500, bsod: 16000 } },
      },
    ]);
  });
});
