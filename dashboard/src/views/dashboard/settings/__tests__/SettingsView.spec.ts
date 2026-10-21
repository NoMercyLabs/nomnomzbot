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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { effectScope, nextTick } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { i18n } from '@/i18n';
import {
  readabilityDefaults,
  readabilityPresets,
  readabilityStorageKey,
  useReadability,
  type Readability,
} from '@/composables/useReadability';

import en from '../i18n/en.json';
import SettingsView from '../SettingsView.vue';

let mounted: VueWrapper | null = null;
let router: Router;

beforeEach(() => {
  localStorage.clear();
  document.documentElement.removeAttribute('style');

  for (const name of document.documentElement.getAttributeNames()) {
    if (name.startsWith('data-rd-') || name === 'data-density') {
      document.documentElement.removeAttribute(name);
    }
  }

  i18n.global.mergeLocaleMessage('en', en);

  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    },
  );
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(): Promise<void> {
  const blank = { template: '<div />' };

  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/settings',
        name: 'dashboard-settings',
        component: blank,
      },
    ],
  });

  await router.push('/app/settings');

  mounted = mount(SettingsView, {
    attachTo: document.body,
    props: { view: 'readability' },
    global: { plugins: [i18n, router] },
  });

  await flushPromises();
}

const stored = (): Readability =>
  JSON.parse(localStorage.getItem(readabilityStorageKey) ?? '{}') as Readability;
const html = (): HTMLElement => document.documentElement;
const button = (label: string): HTMLButtonElement =>
  [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
    (item) => item.textContent?.trim() === label,
  ) as HTMLButtonElement;
const group = (label: string): HTMLElement =>
  document.body.querySelector<HTMLElement>(`[aria-label="${label}"]`) as HTMLElement;

const choose = async (groupLabel: string, optionLabel: string): Promise<void> => {
  const option = [...group(groupLabel).querySelectorAll<HTMLButtonElement>('button')].find(
    (item) => item.textContent?.trim() === optionLabel,
  ) as HTMLButtonElement;

  option.click();
  await flushPromises();
};

describe('Readability presets', () => {
  it('the dyslexia-friendly preset sets every option, stores it and applies it to the page', async () => {
    await open();
    button('Dyslexia-friendly').click();
    await flushPromises();

    expect(stored()).toEqual(readabilityPresets.dyslexia);

    expect(Object.keys(readabilityPresets.dyslexia).sort()).toEqual(
      Object.keys(readabilityDefaults).sort(),
    );

    expect(html().dataset.rdFont).toBe('lexend');
    expect(html().dataset.rdLh).toBe('wide');
    expect(html().dataset.rdSpacing).toBe('wide');
    expect(html().dataset.rdMotion).toBe('reduce');
    expect(html().dataset.density).toBe('comfortable');
    expect(html().style.getPropertyValue('--text-sm')).toBe('16px');
  });

  it('the high contrast preset turns the contrast attribute on and Default turns every option back', async () => {
    await open();
    button('High contrast').click();
    await flushPromises();
    expect(stored().contrast).toBe(true);
    expect(html().dataset.rdContrast).toBe('on');

    button('Default').click();
    await flushPromises();
    expect(stored()).toEqual(readabilityDefaults);
    expect(html().dataset.rdContrast).toBeUndefined();
    expect(html().dataset.density).toBe('compact');
  });
});

const states = (scope: HTMLElement): (string | undefined)[] =>
  [...scope.querySelectorAll<HTMLElement>('button')].map((item) => item.dataset.state);
const rowOf = (title: string): HTMLElement =>
  [...document.body.querySelectorAll<HTMLElement>('[data-view="readability"] .min-h-11')].find(
    (row) => row.textContent?.includes(title),
  ) as HTMLElement;

// Owner 2026-10-20: "readability is not using the right selection component for each option".
describe('Readability controls', () => {
  it('shows the presets as one segmented control with the matching preset chosen', async () => {
    await open();
    const presets = group('Start from a preset');

    expect(presets.hasAttribute('data-segmented')).toBe(true);
    expect(states(presets)).toEqual(['on', 'off', 'off']);

    await choose('Start from a preset', 'Dyslexia-friendly');

    expect(states(presets)).toEqual(['off', 'on', 'off']);
    expect(stored()).toEqual(readabilityPresets.dyslexia);
  });

  it('chooses no preset once the options match none of them', async () => {
    await open();
    await choose('Line spacing', 'Wider');

    expect(states(group('Start from a preset'))).toEqual(['off', 'off', 'off']);
  });

  it('turns reduce motion on and off with a switch, and off follows the system', async () => {
    await open();
    const toggle = rowOf('Reduce motion').querySelector<HTMLElement>('[role="switch"]')!;

    expect(toggle.getAttribute('aria-checked')).toBe('false');

    toggle.click();
    await flushPromises();

    expect(toggle.getAttribute('aria-checked')).toBe('true');
    expect(stored().motion).toBe('reduce');
    expect(html().dataset.rdMotion).toBe('reduce');

    toggle.click();
    await flushPromises();

    expect(stored().motion).toBe('system');
    expect(html().dataset.rdMotion).toBeUndefined();
  });

  it('gives every option the control its values call for', async () => {
    await open();

    const kinds = {
      Font: '[data-segmented]',
      'Text size': '[role="slider"]',
      'Line spacing': '[data-segmented]',
      'Letter and word spacing': '[data-segmented]',
      'High contrast': '[role="switch"]',
      'Reduce motion': '[role="switch"]',
      'Size of controls': '[data-segmented]',
    } as const satisfies Record<string, string>;

    for (const [title, selector] of Object.entries(kinds)) {
      const found = rowOf(title).querySelector(selector) !== null;

      expect({ title, found }).toEqual({ title, found: true });
    }
  });
});

describe('Readability options', () => {
  it('a changed option is stored and applied to the document', async () => {
    await open();
    await choose('Line spacing', 'Wider');
    await choose('Font', 'Legible');

    expect(stored().lh).toBe('wider');
    expect(stored().font).toBe('hyperlegible');
    expect(html().dataset.rdLh).toBe('wider');
    expect(html().dataset.rdFont).toBe('hyperlegible');
    expect(html().style.getPropertyValue('--lh-sm')).toBe(`${Math.round(20 * 1.3)}px`);
  });

  it('shows which option of each choice is on', async () => {
    await open();
    await choose('Line spacing', 'Wider');

    const states = [...group('Line spacing').querySelectorAll<HTMLElement>('button')].map(
      (item) => item.dataset.state,
    );

    expect(states).toEqual(['off', 'off', 'on']);
  });

  // Owner 2026-10-14: "why is there a chat example in the main config when these are all in the chat view
  // settings?" Readability is for the whole dashboard, so its sample is a page heading and body text.
  it('shows a dashboard text sample, a heading and body text, never a chat line', async () => {
    await open();
    const sample: HTMLElement = document.body.querySelector('[data-slot="readability-sample"]')!;
    const text = (sample.textContent ?? '').replace(/\s+/g, ' ').trim();

    expect(sample.querySelector('[data-sample-heading]')?.textContent).toBe('A page heading');
    expect(text).toContain('Body text reads like this on every page.');
    expect(text).not.toContain('chat');
    expect(text).not.toContain('viewer');
  });

  // Re-shoot 2026-10-19: the sample was inset from the row groups under it, and at 1674 px each label sat
  // 1300 px from its control. Decision 2026-10-19: the card takes the container width like its sibling tabs.
  it('lines the sample up with the row groups and sets no card width cap', async () => {
    await open();
    const sample: HTMLElement = document.body.querySelector('[data-slot="readability-sample"]')!;
    const card: HTMLElement = document.body.querySelector('[data-view="readability"]')!;

    expect(sample.classList.contains('mx-3')).toBe(false);
    expect(card.classList.contains('max-w-4xl')).toBe(false);
  });

  // Re-shoot 2026-10-19 at 1280: the section headings sat 16 px inside the option panels under them.
  it('lines every section heading up with the panel edge, with no inset of its own', async () => {
    await open();
    const headings: HTMLElement[] = [
      ...document.body.querySelectorAll<HTMLElement>('[data-view="readability"] h3'),
    ];

    expect(headings).toHaveLength(4);

    for (const heading of headings) {
      expect(heading.parentElement!.classList.contains('px-4')).toBe(false);
    }

    const presets = group('Start from a preset');

    expect(presets.parentElement!.classList.contains('px-4')).toBe(false);
    expect(presets.classList.contains('px-4')).toBe(false);
  });

  it('a text size scales every text token', async () => {
    const scope = effectScope();
    const reading = scope.run(() => useReadability())!;

    reading.settings.value = { ...reading.settings.value, size: 18 };
    await nextTick();

    expect(html().style.getPropertyValue('--text-sm')).toBe('18px');
    expect(html().style.getPropertyValue('--text-xs')).toBe('15.43px');
    scope.stop();
  });

  it('a reload keeps the stored options and applies them again', () => {
    localStorage.setItem(
      readabilityStorageKey,
      JSON.stringify({
        ...readabilityDefaults,
        font: 'lexend',
        size: 17,
        contrast: true,
      }),
    );

    const scope = effectScope();
    const reading = scope.run(() => useReadability())!;

    expect(reading.settings.value.font).toBe('lexend');
    expect(reading.settings.value.size).toBe(17);
    expect(html().dataset.rdFont).toBe('lexend');
    expect(html().dataset.rdContrast).toBe('on');
    scope.stop();
  });

  it('every option names its default, taken from the defaults table', async () => {
    await open();
    const text = document.body.textContent ?? '';

    for (const note of [
      'Default: Inter.',
      'Default: 14 px.',
      'Default: Normal.',
      'Default: Compact.',
    ]) {
      expect(text).toContain(note);
    }

    // High contrast and Reduce motion are both off by default, so two rows carry the same note.
    expect(text.match(/Default: Off\./g)).toHaveLength(2);
  });

  it('the panel does not scroll inside the page, so a phone has one scroll only', async () => {
    await open();
    const card = document.body.querySelector('[data-view="readability"]') as HTMLElement;

    expect(card.classList.contains('flex-none')).toBe(true);
  });

  it('says the options are saved on this device, never on the account', async () => {
    await open();
    expect(document.body.textContent).toContain('saved on this device');
    expect(document.body.textContent?.toLowerCase()).not.toContain('account');
  });
});
