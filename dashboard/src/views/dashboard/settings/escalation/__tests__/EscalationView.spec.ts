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
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { computed } from 'vue';
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';
import { settingsNav } from '@/layouts/dashboard/settingsNavigation';
import { dashboardSettingsRoutes } from '../../route';

import en from '../i18n/en.json';
import EscalationView from '../EscalationView.vue';

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

const access = vi.hoisted(() => ({ write: true }));

vi.mock('@/api/client', () => ({ client: http }));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1') }),
}));

vi.mock('@/composables/useCan', () => ({
  useCan: () => ({ can: (key: string) => key !== 'moderation:escalation:write' || access.write }),
}));

const defaults = {
  isEnabled: false,
  ladder: [
    {
      atOffense: 1,
      action: 'warn',
      timeoutSeconds: null,
    },
  ],
  offenseWindowHours: 24,
  countAutoModViolations: true,
  defaultTimeoutSeconds: 600,
};

const limits = {
  minOffenseWindowHours: 1,
  maxOffenseWindowHours: 8760,
  minTimeoutSeconds: 1,
  maxTimeoutSeconds: 1209600,
  minLadderSteps: 1,
  maxLadderSteps: 50,
};

const stored = {
  isEnabled: true,
  ladder: [
    {
      atOffense: 1,
      action: 'warn',
      timeoutSeconds: null,
    },
    {
      atOffense: '3',
      action: 'timeout',
      timeoutSeconds: '300',
    },
  ],
  offenseWindowHours: '48',
  countAutoModViolations: false,
  defaultTimeoutSeconds: 120,
  defaults,
  limits,
};

const ok = (value: unknown): Answer => ({
  data: { data: value },
  response: { ok: true, status: 200 },
});

let mounted: VueWrapper | null = null;

async function open(): Promise<void> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: EscalationView }],
  });

  mounted = mount(EscalationView, {
    attachTo: document.body,
    global: { plugins: [i18n, router, createPinia(), [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();
}

const field = (name: string): HTMLElement =>
  document.body.querySelector<HTMLElement>(`[data-field="${name}"]`)!;

const step = (index: number): HTMLElement =>
  document.body.querySelector<HTMLElement>(`[data-step="${index}"]`)!;

const action = (name: string): HTMLButtonElement =>
  document.body.querySelector<HTMLButtonElement>(`[data-action="${name}"]`)!;

const stepInput = (index: number, name: string): HTMLInputElement =>
  step(index).querySelector<HTMLInputElement>(`[data-step-field="${name}"]`)!;

async function typeInto(input: HTMLInputElement, value: string): Promise<void> {
  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  await flushPromises();
}

const stepCount = (): number => document.body.querySelectorAll('[data-step]').length;

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
  access.write = true;
  http.GET.mockReset();
  http.PUT.mockReset();
  http.GET.mockResolvedValue(ok(stored));

  http.PUT.mockImplementation(async (_path: string, init: unknown) =>
    ok((init as { body: object }).body),
  );
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

describe('Escalation policy page', () => {
  it('shows the stored policy with numbers or text alike, and the default of each setting', async () => {
    await open();

    expect(field('isEnabled').querySelector('[role="switch"]')?.getAttribute('aria-checked')).toBe(
      'true',
    );

    expect(
      field('countAutoModViolations')
        .querySelector('[role="switch"]')
        ?.getAttribute('aria-checked'),
    ).toBe('false');

    expect(field('offenseWindowHours').querySelector('input')!.value).toBe('48');

    expect(field('offenseWindowHours').querySelector('[data-row-hint]')?.textContent).toContain(
      '24',
    );

    expect(field('offenseWindowHours').querySelector('input')!.max).toBe('8760');
    expect(stepCount()).toBe(2);
    expect(stepInput(1, 'atOffense').value).toBe('3');
    expect(stepInput(1, 'timeoutSeconds').value).toBe('5:00');
  });

  it('sends the whole policy to the escalation route on Save', async () => {
    await open();
    await typeInto(field('offenseWindowHours').querySelector('input')!, '72');

    action('save').click();
    await flushPromises();

    const call = http.PUT.mock.calls[0]!;
    const init = call[1] as {
      params: { path: { channelId: string } };
      body: Record<string, unknown>;
    };

    expect(call[0]).toBe('/api/v{version}/channels/{channelId}/moderation/escalation');
    expect(init.params.path.channelId).toBe('c1');

    expect(init.body).toEqual({
      isEnabled: true,
      countAutoModViolations: false,
      offenseWindowHours: 72,
      defaultTimeoutSeconds: 120,
      ladder: [
        {
          atOffense: 1,
          action: 'warn',
          timeoutSeconds: null,
        },
        {
          atOffense: 3,
          action: 'timeout',
          timeoutSeconds: 300,
        },
      ],
    });
  });

  it('adds a step after the last offense number and removes a step', async () => {
    await open();

    action('add-step').click();
    await flushPromises();

    expect(stepCount()).toBe(3);
    expect(stepInput(2, 'atOffense').value).toBe('4');

    step(0).querySelector<HTMLButtonElement>('[data-action="remove-step"]')!.click();
    await flushPromises();

    expect(stepCount()).toBe(2);
    expect(stepInput(0, 'atOffense').value).toBe('3');
  });

  it('switches a step between warn, timeout and ban and gives a timeout the default duration', async () => {
    await open();

    step(0).querySelector<HTMLButtonElement>('[data-segment][value="timeout"]')!.click();
    await flushPromises();

    expect(stepInput(0, 'timeoutSeconds').value).toBe('2:00');

    step(0).querySelector<HTMLButtonElement>('[data-segment][value="ban"]')!.click();
    await flushPromises();

    expect(step(0).querySelector('[data-step-field="timeoutSeconds"]')).toBeNull();

    action('save').click();
    await flushPromises();

    const body = (http.PUT.mock.calls[0]![1] as { body: { ladder: unknown[] } }).body;

    expect(body.ladder[0]).toEqual({
      atOffense: 1,
      action: 'ban',
      timeoutSeconds: null,
    });
  });

  it('puts a refusal on the failing field, in words, and keeps the draft', async () => {
    http.PUT.mockResolvedValue({
      error: {
        status: 'error',
        message: 'x',
        code: 'VALIDATION_FAILED',
        data: {
          key: 'moderation.escalation.error.timeoutStepOutOfRange',
          field: 'ladder[1].timeoutSeconds',
        },
      },
      response: { ok: false, status: 400 },
    });

    await open();
    await typeInto(stepInput(1, 'atOffense'), '4');

    action('save').click();
    await flushPromises();

    const message = step(1).querySelector('[data-row-error]')?.textContent;

    expect(message).toBe('A timeout step needs a length inside the allowed range.');
    expect(step(0).querySelector('[data-row-error]')).toBeNull();
    expect(stepInput(1, 'atOffense').value).toBe('4');
  });

  it('locks every field and offers no Save to a person without the write right', async () => {
    access.write = false;
    await open();

    expect(
      document.body
        .querySelector('fieldset[data-slot="read-only-scope"]')
        ?.hasAttribute('disabled'),
    ).toBe(true);

    expect(document.body.querySelector('[data-action="save"]')).toBeNull();
  });
});

describe('Escalation route', () => {
  it('the sidebar entry resolves to the page and is gated by the read right', () => {
    const entry = settingsNav
      .flatMap((group) => group.entries)
      .find((item) => item.route === 'dashboard-settings-escalation');
    const record = dashboardSettingsRoutes.find(
      (route) => route.name === 'dashboard-settings-escalation',
    );

    expect(entry).toBeDefined();
    expect(record!.path).toBe('settings/escalation');

    expect(record!.meta).toMatchObject({
      navMode: 'settings',
      action: 'moderation:escalation:read',
    });
  });
});
