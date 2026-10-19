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

import { i18n } from '@/i18n';

import en from '../i18n/en.json';
import { accessPath } from './heldKeys';
import EngagementView from '../EngagementView.vue';

type Body = Record<string, unknown>;
type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

const saved: Body[] = [];
let config: Body = {};
let refusal: string | undefined;
let held: string[] = [];

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string): Promise<Answer> => ({
      data: { data: path === accessPath
        ? { heldActionKeys: held }
        : config },
      response: { status: 200, ok: true },
    }),
    PUT: async (_path: string, options: { body: Body }): Promise<Answer> => {
      saved.push(options.body);

      if (refusal !== undefined)
        return { error: { detail: refusal }, response: { status: 400, ok: false } };

      config = options.body;

      return { data: { data: config }, response: { status: 200, ok: true } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;

beforeEach(() => {
  saved.length = 0;
  refusal = undefined;
  held = ['engagement:read', 'engagement:write'];

  config = {
    firstTimeChatterEnabled: true,
    returningChatterEnabled: false,
    watchStreakEnabled: false,
    streakMilestones: [5, 10],
    greetCooldownSeconds: 30,
  };

  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(): Promise<void> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(EngagementView, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
}

const q = <T extends Element>(selector: string): T => document.body.querySelector<T>(selector) as T;
const saveButton = (): HTMLButtonElement => q<HTMLButtonElement>('button[form="engagement-form"]');
const milestoneInputs = (): HTMLInputElement[] => [
  ...document.body.querySelectorAll<HTMLInputElement>('[data-milestone]'),
];

const switchFor = (title: string): HTMLElement => {
  const path = `//label[.//span[normalize-space(text())="${title}"]]//button[@role="switch"]`;
  const found = document.evaluate(path, document, null, XPathResult.FIRST_ORDERED_NODE_TYPE, null);

  return found.singleNodeValue as HTMLElement;
};

async function type(el: HTMLInputElement, value: string): Promise<void> {
  el.value = value;
  el.dispatchEvent(new Event('input', { bubbles: true }));
  await flushPromises();
}

async function click(el: HTMLElement): Promise<void> {
  el.click();
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
}

describe('Greetings and streaks', () => {
  it('shows the saved config and keeps Save off until something changes', async () => {
    await open();

    expect(switchFor('First message ever').getAttribute('aria-checked')).toBe('true');
    expect(switchFor('Back after a break').getAttribute('aria-checked')).toBe('false');
    expect(q<HTMLInputElement>('[data-cooldown]').value).toBe('30');
    expect(milestoneInputs().map((input) => input.value)).toEqual(['5', '10']);
    expect(saveButton().disabled).toBe(true);
  });

  it('shows the saved config to a person without the write key but offers no way to change it', async () => {
    held = ['engagement:read'];
    await open();

    expect(switchFor('First message ever').getAttribute('aria-checked')).toBe('true');
    expect(switchFor('First message ever').hasAttribute('disabled')).toBe(true);
    expect(q<HTMLInputElement>('[data-cooldown]').disabled).toBe(true);
    expect(document.body.querySelector('button[form="engagement-form"]')).toBeNull();
    expect(document.body.querySelector('[data-add-milestone]')).toBeNull();
    expect(document.body.querySelector('[data-remove-milestone]')).toBeNull();
  });

  it('sends the edited config in one PUT and shows the saved state after', async () => {
    await open();
    await click(switchFor('Watch streaks'));
    await type(q<HTMLInputElement>('[data-cooldown]'), '90');
    await click(q('[data-add-milestone]'));
    await type(milestoneInputs()[2] as HTMLInputElement, '25');

    expect(saveButton().disabled).toBe(false);

    await click(saveButton());

    expect(saved).toEqual([
      {
        firstTimeChatterEnabled: true,
        returningChatterEnabled: false,
        watchStreakEnabled: true,
        streakMilestones: [5, 10, 25],
        greetCooldownSeconds: 90,
      },
    ]);

    expect(saveButton().disabled).toBe(true);
  });

  it('removes a milestone from the list that is sent', async () => {
    await open();
    await click(q('[data-remove-milestone]'));
    await click(saveButton());

    expect(saved[0]?.streakMilestones).toEqual([10]);
  });

  it('blocks Save and names the problem while a number is not valid', async () => {
    await open();
    await type(milestoneInputs()[0] as HTMLInputElement, '0');

    expect(saveButton().disabled).toBe(true);
    expect(document.body.textContent).toContain('Fix the numbers marked in red to save.');

    await type(milestoneInputs()[0] as HTMLInputElement, '3');
    await type(q<HTMLInputElement>('[data-cooldown]'), '-5');

    expect(saveButton().disabled).toBe(true);
    expect(saved).toEqual([]);
  });

  it('shows the reason the bot gives when it refuses the config', async () => {
    refusal = 'GreetCooldownSeconds cannot be negative.';

    await open();
    await type(q<HTMLInputElement>('[data-cooldown]'), '5');
    await click(saveButton());

    expect(q('[role="alert"]').textContent).toContain('GreetCooldownSeconds cannot be negative.');
  });

  it('names the default of every setting next to it', async () => {
    await open();

    const text = document.body.textContent ?? '';

    expect(text.match(/Default: off\./g)).toHaveLength(3);
    expect(text).toContain('Default: 5 seconds.');
    expect(text).toContain('Default: none, which celebrates every stream.');
  });

  it('puts the defaults in the form on reset and saves them only on Save', async () => {
    await open();
    await click(q('[data-reset-defaults]'));

    expect(saved).toEqual([]);
    expect(switchFor('First message ever').getAttribute('aria-checked')).toBe('false');
    expect(q<HTMLInputElement>('[data-cooldown]').value).toBe('5');
    expect(milestoneInputs()).toHaveLength(0);
    expect(q<HTMLButtonElement>('[data-reset-defaults]').disabled).toBe(true);

    await click(saveButton());

    expect(saved).toEqual([
      {
        firstTimeChatterEnabled: false,
        returningChatterEnabled: false,
        watchStreakEnabled: false,
        streakMilestones: [],
        greetCooldownSeconds: 5,
      },
    ]);
  });

  it('offers no reset to a person without the write key', async () => {
    held = ['engagement:read'];
    await open();

    expect(document.body.querySelector('[data-reset-defaults]')).toBeNull();
  });
});
