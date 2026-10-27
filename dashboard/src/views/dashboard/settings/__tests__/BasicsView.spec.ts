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
import { createPinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { computed } from 'vue';

import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import BasicsView from '../BasicsView.vue';
import { answerHeldKeys } from './heldKeys';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  PUT: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  use: (): undefined => undefined,
}));

vi.mock('@/api/client', () => ({ client: http }));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1') }),
}));

const stored = {
  prefix: '!',
  botLinePrefix: null,
  locale: 'en',
  autoJoin: false,
  timezone: 'Europe/Amsterdam',
  keepActiveWhileOffline: false,
  viewerCasualFrom: 10,
  viewerRegularFrom: 100,
  viewerVeteranFrom: 500,
};

let mounted: VueWrapper | null = null;
let held: string[] = [];

async function open(): Promise<void> {
  const queries: QueryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(BasicsView, {
    attachTo: document.body,
    global: { plugins: [i18n, createPinia(), [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();
}

const saveButton = (): HTMLButtonElement =>
  [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
    (item) => item.textContent?.trim() === 'Save',
  ) as HTMLButtonElement;
const prefixInput = (): HTMLInputElement =>
  document.body.querySelector<HTMLInputElement>('input[aria-label="Command prefix"]') as HTMLInputElement;

const stepInput = (label: string): HTMLInputElement =>
  document.body.querySelector<HTMLInputElement>(`input[aria-label="${label}"]`) as HTMLInputElement;

async function type(input: HTMLInputElement, value: string): Promise<void> {
  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  await flushPromises();
}

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
  http.GET.mockReset();
  http.PUT.mockReset();
  held = ['setup:read', 'setup:write'];
  http.GET.mockResolvedValue({ data: { data: stored }, response: { ok: true, status: 200 } });

  answerHeldKeys(http.GET, () => held);

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

describe('Basics settings', () => {
  it('never shrinks the panel inside the page body, so the page scrolls once and the panel does not', async () => {
    await open();

    expect(document.body.querySelector('[data-view="basics"]')?.classList.contains('shrink-0')).toBe(true);
  });

  it('fills the fields from the bot and keeps Save off until something changes', async () => {
    await open();

    expect(prefixInput().value).toBe('!');
    expect(saveButton().disabled).toBe(true);
  });

  it('shows the saved values to a person without the setup write key but offers no Save or edit', async () => {
    held = ['setup:read'];
    await open();

    expect(prefixInput().value).toBe('!');
    expect(prefixInput().disabled).toBe(true);

    expect(
      [...document.body.querySelectorAll('button')].some((item) => item.textContent?.trim() === 'Save'),
    ).toBe(false);
  });

  it('saves the edited prefix with the other stored values', async () => {
    http.PUT.mockResolvedValue({
      data: { data: { ...stored, prefix: '?' } },
      response: { ok: true, status: 200 },
    });

    await open();
    await type(prefixInput(), '?');

    expect(saveButton().disabled).toBe(false);

    saveButton().click();
    await flushPromises();

    const [, init] = http.PUT.mock.calls[0] as [string, { body: unknown }];

    expect(init.body).toEqual({
      prefix: '?',
      botLinePrefix: '',
      locale: 'en',
      timezone: 'Europe/Amsterdam',
      autoJoin: false,
      keepActiveWhileOffline: false,
      viewerCasualFrom: 10,
      viewerRegularFrom: 100,
      viewerVeteranFrom: 500,
    });

    expect(saveButton().disabled).toBe(true);
  });

  it('shows the three stored activity steps', async () => {
    await open();

    expect(stepInput('Casual from').value).toBe('10');
    expect(stepInput('Regular from').value).toBe('100');
    expect(stepInput('Veteran from').value).toBe('500');
  });

  it('saves an edited activity step as numbers next to the other steps', async () => {
    http.PUT.mockResolvedValue({
      data: { data: { ...stored, viewerRegularFrom: 250 } },
      response: { ok: true, status: 200 },
    });

    await open();
    await type(stepInput('Regular from'), '250');

    expect(saveButton().disabled).toBe(false);

    saveButton().click();
    await flushPromises();

    const [, init] = http.PUT.mock.calls[0] as [string, { body: Record<string, unknown> }];

    expect(init.body.viewerCasualFrom).toBe(10);
    expect(init.body.viewerRegularFrom).toBe(250);
    expect(init.body.viewerVeteranFrom).toBe(500);
    expect(saveButton().disabled).toBe(true);
  });

  it('blocks Save and says why when casual reaches regular', async () => {
    await open();
    await type(stepInput('Casual from'), '100');

    expect(saveButton().disabled).toBe(true);
    expect(document.body.textContent).toContain('Each step must be higher than the one before it.');
    expect(http.PUT).not.toHaveBeenCalled();
  });

  it('blocks Save for an empty step and for a veteran step above one million', async () => {
    await open();
    await type(stepInput('Casual from'), '');

    expect(saveButton().disabled).toBe(true);

    await type(stepInput('Casual from'), '10');
    await type(stepInput('Veteran from'), '1000001');

    expect(saveButton().disabled).toBe(true);
    expect(document.body.textContent).toContain('Use a whole number from 1 to 1,000,000.');
  });

  it('refuses a prefix with a space and never sends it', async () => {
    await open();
    await type(prefixInput(), 'a b');

    expect(saveButton().disabled).toBe(true);
    expect(http.PUT).not.toHaveBeenCalled();
  });

  it('flips auto-join through its switch and sends the new value', async () => {
    http.PUT.mockResolvedValue({
      data: { data: { ...stored, autoJoin: true } },
      response: { ok: true, status: 200 },
    });

    await open();

    document.body.querySelector<HTMLButtonElement>('button[role="switch"]')?.click();
    await flushPromises();
    saveButton().click();
    await flushPromises();

    const [, init] = http.PUT.mock.calls[0] as [string, { body: { autoJoin: boolean } }];

    expect(init.body.autoJoin).toBe(true);
  });
});
