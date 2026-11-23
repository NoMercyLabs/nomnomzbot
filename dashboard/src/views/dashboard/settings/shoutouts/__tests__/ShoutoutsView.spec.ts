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

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type DOMWrapper, type VueWrapper } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { computed } from 'vue';

import { chooseTarget, openTargetPicker } from '@/components/layout/__tests__/targetPickerDom';
import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import ShoutoutsView from '../ShoutoutsView.vue';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};

const autoPath = '/api/v{version}/channels/{channelId}/moderation/auto-shoutout';
const templatePath = '/api/v{version}/channels/{channelId}/moderation/shoutout-template';
const commandsPath = '/api/v{version}/channels/{channelId}/commands';

const store = vi.hoisted(() => ({
  enabled: false,
  template: null as string | null,
  target: null as { kind: number; id: string } | null,
}));
const access = vi.hoisted(() => ({ write: true }));
const puts = vi.hoisted(() => [] as { path: string; body: unknown }[]);

vi.mock('@/api/client', () => ({
  client: {
    use: (): undefined => undefined,
    GET: async (path: string): Promise<Answer> => {
      const answers = new Map<string, unknown>([
        [autoPath, { enabled: store.enabled }],
        [templatePath, { template: store.template, target: store.target }],
        [commandsPath, [{ id: 'c1', name: 'hello' }]],
      ]);

      // Anything else is the helper list the template field asks for: none.
      return { data: { data: answers.get(path) ?? [] }, response: { ok: true, status: 200 } };
    },
    PUT: async (path: string, init: { body: Record<string, unknown> }): Promise<Answer> => {
      puts.push({ path, body: init.body });

      if (path.endsWith('auto-shoutout')) {
        store.enabled = init.body.enabled as boolean;
      } else {
        store.template = (init.body.template as string) === ''
          ? null
          : (init.body.template as string);

        // The server clears a binding with a pipeline that has no id; a missing target leaves it as it is.
        const sent = init.body.target as { kind: number; id: string } | undefined;

        if (sent !== undefined) {
          store.target = sent.kind === 1 && sent.id === ''
            ? null
            : sent;
        }
      }

      return { response: { ok: true, status: 204 } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1') }),
}));

vi.mock('@/composables/useCan', () => ({
  useCan: () => ({ can: (key: string) => key !== 'moderation:shoutout' || access.write }),
}));

vi.mock('vue-sonner', () => ({
  toast: { success: vi.fn<() => void>(), error: vi.fn<() => void>() },
}));

let wrapper: VueWrapper | undefined;

async function open(): Promise<VueWrapper> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  wrapper = mount(ShoutoutsView, {
    global: { plugins: [i18n, createPinia(), [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();

  return wrapper;
}

const modeButton = (page: VueWrapper, label: string): DOMWrapper<Element> =>
  page.findAll('[data-reply-mode] button').find((b) => b.text().trim() === label)!;

const area = (page: VueWrapper): HTMLTextAreaElement =>
  page.get('[data-field="shoutoutTemplate"]').element as HTMLTextAreaElement;

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
  store.enabled = false;
  store.template = 'Go follow {target.displayname}!';
  store.target = null;
  access.write = true;
  puts.length = 0;
});

afterEach(() => {
  wrapper?.unmount();
  vi.clearAllMocks();
});

describe('ShoutoutsView', () => {
  it('shows the stored message and keeps Save off until it changes', async () => {
    const page = await open();

    expect(area(page).value).toBe('Go follow {target.displayname}!');
    expect(page.get('[data-action="save"]').attributes('disabled')).toBeDefined();
  });

  // Owner 2026-11-19: Save sticks to the bottom of the scroll area and is primary only while a change waits.
  it('keeps Save in a sticky footer and turns it primary only while the message changed', async () => {
    const page = await open();
    const footer = page.get('[data-action="save"]').element.closest('footer');

    expect(footer?.classList.contains('sticky')).toBe(true);
    expect(footer?.classList.contains('bottom-0')).toBe(true);
    expect(page.get('[data-action="save"]').attributes('data-variant')).toBe('outline');

    await page.get('[data-field="shoutoutTemplate"]').setValue('Hey {target.displayname}');

    expect(page.get('[data-action="save"]').attributes('data-variant')).toBe('default');
  });

  it('saves the edited message trimmed and shows the stored value after the refetch', async () => {
    const page = await open();

    await page.get('[data-field="shoutoutTemplate"]').setValue('  Hey {target.displayname}  ');
    await page.get('[data-action="save"]').trigger('click');
    await flushPromises();

    expect(puts).toEqual([{ path: templatePath, body: { template: 'Hey {target.displayname}' } }]);
    expect(store.template).toBe('Hey {target.displayname}');
  });

  it('clears the custom message with Use the default', async () => {
    const page = await open();

    await page.get('[data-action="use-default"]').trigger('click');
    await flushPromises();

    expect(puts).toEqual([{ path: templatePath, body: { template: '' } }]);
    expect(store.template).toBeNull();
    expect(page.get('[data-action="use-default"]').attributes('disabled')).toBeDefined();
  });

  it('saves a picked command as the shoutout target and shows it after the refetch', async () => {
    Element.prototype.hasPointerCapture = (): boolean => false;
    Element.prototype.releasePointerCapture = (): void => undefined;
    Element.prototype.scrollIntoView = (): void => undefined;
    const page = await open();

    await modeButton(page, 'Pipeline').trigger('click');
    await openTargetPicker(page.element);
    await chooseTarget('!hello');
    await page.get('[data-action="save"]').trigger('click');
    await flushPromises();

    expect(puts).toEqual([
      {
        path: templatePath,
        body: { template: 'Go follow {target.displayname}!', target: { kind: 0, id: 'c1' } },
      },
    ]);

    expect(store.target).toEqual({ kind: 0, id: 'c1' });
    expect(page.find('[data-field="shoutoutTemplate"]').exists()).toBe(false);
    expect(page.get('[data-target-picker]').text()).toContain('!hello');
  });

  it('opens a shoutout that runs a target on that target and hides the message', async () => {
    store.target = { kind: 0, id: 'c1' };
    const page = await open();

    expect(page.find('[data-field="shoutoutTemplate"]').exists()).toBe(false);
    expect(page.get('[data-target-picker]').text()).toContain('!hello');
  });

  it('clears the target when the person switches back to the message and saves', async () => {
    store.target = { kind: 0, id: 'c1' };
    const page = await open();

    await modeButton(page, 'Simple reply').trigger('click');
    await page.get('[data-action="save"]').trigger('click');
    await flushPromises();

    expect(puts).toEqual([
      {
        path: templatePath,
        body: { template: 'Go follow {target.displayname}!', target: { kind: 1, id: '' } },
      },
    ]);

    expect(store.target).toBeNull();
  });

  it('turns automatic shoutouts on at once', async () => {
    const page = await open();

    await page.get('[data-field="autoShoutout"] button[role="switch"]').trigger('click');
    await flushPromises();

    expect(puts).toEqual([{ path: autoPath, body: { enabled: true } }]);
    expect(store.enabled).toBe(true);
  });

  it('is read-only without the shoutout right', async () => {
    access.write = false;
    const page = await open();

    expect(page.find('[data-action="save"]').exists()).toBe(false);
    expect(page.find('[data-action="use-default"]').exists()).toBe(false);
  });
});
