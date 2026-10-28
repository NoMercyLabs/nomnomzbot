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
import { computed } from 'vue';

import { featuresKey } from '@/api/features';
import { i18n } from '@/i18n';
import { settingsNav } from '@/layouts/dashboard/settingsNavigation';
import { configDomainKeys } from '@/realtime/configDomains';

import { dashboardSettingsRoutes } from '../../route';
import { answerHeldKeys } from '../../__tests__/heldKeys';
import en from '../i18n/en.json';
import FeaturesView from '../FeaturesView.vue';

type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};

type Feature = {
  featureKey: string;
  label: string;
  description: string;
  isEnabled: boolean;
  enabledAt?: string | null;
  requiredScopes: string[];
  entitled: boolean;
  entitlementReason?: string | null;
  requiredTier?: string | null;
};

const http = vi.hoisted(() => ({
  GET: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  POST: vi.fn<(path: string, init: unknown) => Promise<Answer>>(),
  use: (): undefined => undefined,
}));

vi.mock('@/api/client', () => ({ client: http }));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1') }),
}));

const listPath = '/api/v{version}/channels/{channelId}/features';
const togglePath = '/api/v{version}/channels/{channelId}/features/{featureKey}/toggle';

const ok = (data: unknown): Answer => ({ data: { data }, response: { ok: true, status: 200 } });

let features: Feature[] = [];
let held: string[] = [];
let mounted: VueWrapper | null = null;

beforeEach(() => {
  http.GET.mockReset();
  http.POST.mockReset();

  features = [
    {
      featureKey: 'tts',
      label: 'Text to speech',
      description: 'Reads chat out loud.',
      isEnabled: true,
      enabledAt: '2026-10-01T10:00:00Z',
      requiredScopes: [],
      entitled: true,
    },
    {
      featureKey: 'clips',
      label: 'Clip creation',
      description: 'Makes clips from chat.',
      isEnabled: false,
      requiredScopes: ['clips:edit', 'channel:manage:clips'],
      entitled: true,
    },
    {
      featureKey: 'ai',
      label: 'AI replies',
      description: 'Answers questions.',
      isEnabled: false,
      requiredScopes: [],
      entitled: false,
      entitlementReason: 'Needs a higher plan.',
      requiredTier: 'Pro',
    },
  ];

  held = ['feature:read', 'feature:write'];
  http.GET.mockImplementation(async () => ok(features));

  answerHeldKeys(http.GET, () => held);

  http.POST.mockImplementation(async (_path, init) => {
    const key = (init as { params: { path: { featureKey: string } } }).params.path.featureKey;
    const row = features.find((feature) => feature.featureKey === key) as Feature;

    row.isEnabled = !row.isEnabled;

    return ok(row);
  });

  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(): Promise<QueryClient> {
  const queries = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  mounted = mount(FeaturesView, {
    attachTo: document.body,
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient: queries }]] },
  });

  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();

  return queries;
}

const row = (key: string): HTMLElement =>
  document.body.querySelector<HTMLElement>(`[data-feature="${key}"]`) as HTMLElement;
const switchOf = (key: string): HTMLButtonElement =>
  row(key).querySelector<HTMLButtonElement>('button[role="switch"]') as HTMLButtonElement;

async function click(el: HTMLElement): Promise<void> {
  el.click();
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
}

describe('Features page', () => {
  it('lists every feature with its state, scopes and entitlement', async () => {
    await open();

    expect(http.GET).toHaveBeenCalledWith(
      listPath,
      { params: { path: { channelId: 'c1', version: '1' } } },
    );

    expect(row('tts').textContent).toContain('Text to speech');
    expect(row('tts').textContent).toContain('Reads chat out loud.');
    expect(switchOf('tts').getAttribute('aria-checked')).toBe('true');
    expect(switchOf('clips').getAttribute('aria-checked')).toBe('false');
    expect(row('clips').textContent).toContain('clips:edit');
    expect(row('clips').textContent).toContain('channel:manage:clips');
    expect(row('ai').textContent).toContain('Needs a higher plan.');
    expect(row('ai').textContent).toContain('Pro');
    expect(switchOf('ai').disabled).toBe(true);
  });

  it('groups the features by area in a fixed order and puts unknown keys last', async () => {
    const make = (featureKey: string, label: string): Feature => ({
      featureKey,
      label,
      description: `${label} text.`,
      isEnabled: false,
      requiredScopes: [],
      entitled: true,
    });

    features = [
      make('tts', 'Text to speech'),
      make('custom_code', 'Custom Code'),
      make('use_7tv', '7TV Emotes'),
      make('use_chat_html', 'Chat HTML Fragments'),
      make('use_viewer_templates', 'Viewer templates'),
    ];

    await open();

    const areas = Array.from(document.body.querySelectorAll<HTMLElement>('[data-area]'));
    const keysIn = (area: HTMLElement): (string | null)[] =>
      Array.from(area.querySelectorAll('[data-feature]')).map((item) => item.getAttribute('data-feature'));

    expect(areas.map((area) => area.getAttribute('data-area'))).toEqual(['chat', 'automation', 'other']);
    expect(keysIn(areas[0] as HTMLElement)).toEqual(['use_7tv', 'use_chat_html', 'use_viewer_templates']);
    expect(keysIn(areas[1] as HTMLElement)).toEqual(['custom_code']);
    expect(keysIn(areas[2] as HTMLElement)).toEqual(['tts']);
    expect(areas[0]?.querySelector('h3')?.textContent).toBe(en.dashboard.settings.features.areas.chat);
    expect(areas[1]?.querySelector('h3')?.textContent).toBe(en.dashboard.settings.features.areas.automation);
  });

  it('toggles one feature with a POST for its key and shows the new state', async () => {
    await open();
    await click(switchOf('clips'));

    expect(http.POST).toHaveBeenCalledTimes(1);

    expect(http.POST).toHaveBeenCalledWith(
      togglePath,
      { params: { path: {
        channelId: 'c1',
        featureKey: 'clips',
        version: '1',
      } } },
    );

    expect(switchOf('clips').getAttribute('aria-checked')).toBe('true');
    expect(switchOf('tts').getAttribute('aria-checked')).toBe('true');
  });

  it('never sends a toggle for a feature the plan does not include', async () => {
    await open();
    await click(switchOf('ai'));

    expect(http.POST).not.toHaveBeenCalled();
  });

  it('shows the state but no switch without the feature write key', async () => {
    held = ['feature:read'];
    await open();

    expect(document.body.querySelector('button[role="switch"]')).toBeNull();
    const states = Array.from(document.body.querySelectorAll('[data-slot="enabled-state"]'));

    expect(states.map((state) => state.getAttribute('data-state'))).toEqual(['on', 'off', 'off']);
    expect(http.POST).not.toHaveBeenCalled();
  });

  it('shows the refusal reason when the toggle fails', async () => {
    await open();

    http.POST.mockResolvedValueOnce({
      error: { detail: 'Connect Twitch first.' },
      response: { ok: false, status: 400 },
    });

    await click(switchOf('clips'));

    expect(document.body.querySelector('[role="alert"]')?.textContent).toContain('Connect Twitch first.');
    expect(switchOf('clips').getAttribute('aria-checked')).toBe('false');
  });

  it('shows the error state with a retry when the list cannot load', async () => {
    http.GET.mockResolvedValue({ error: {}, response: { ok: false, status: 500 } });
    await open();

    expect(document.body.textContent).toContain(en.dashboard.settings.features.loadFailed);
  });

  it('refreshes when the bot publishes a features change', () => {
    const builders = configDomainKeys.features ?? [];

    expect(builders.map((build) => build('c1'))).toContainEqual(featuresKey('c1'));
  });
});

describe('Features route', () => {
  it('resolves the sidebar entry to the Features page and not a scaffold', () => {
    const entry = settingsNav.flatMap((group) => group.entries).find((item) => item.route === 'dashboard-settings-features');
    const record = dashboardSettingsRoutes.find((route) => route.name === 'dashboard-settings-features');

    expect(entry).toBeDefined();
    expect(record?.path).toBe('settings/features');
    expect(record?.meta).toEqual({ navMode: 'settings', action: 'feature:read' });
    expect(record?.component).toBeTypeOf('function');
  });
});
