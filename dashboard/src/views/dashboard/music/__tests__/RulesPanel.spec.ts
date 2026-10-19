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
import { createPinia, setActivePinia, type Pinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ref } from 'vue';

import { i18n } from '@/i18n';

import RulesPanel from '../components/RulesPanel.vue';
import en from '../i18n/en.json';

type Options = { params?: { path?: Record<string, unknown> }; body?: unknown };
type Call = {
  method: string;
  path: string;
  blockedTrackId: unknown;
  body: unknown;
};
type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

const calls: Call[] = [];
let config: Record<string, unknown> | null = null;
let blocked: Record<string, unknown>[] = [];
const playlists: Record<string, unknown>[] = [
  {
    id: 'p1',
    name: 'Bangers',
    uri: 'spotify:playlist:p1',
    trackCount: 12,
    provider: 'spotify',
  },
  {
    id: 'p2',
    name: 'Chill',
    uri: 'spotify:playlist:p2',
    trackCount: 3,
    provider: 'spotify',
  },
];

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } });

const baseConfig = () =>
  ({
    isEnabled: true,
    preferredProvider: 'spotify',
    maxQueueSize: 20,
    maxRequestsPerUser: 1,
    allowYouTube: false,
    allowSpotify: true,
    minTrustLevel: 'moderators',
    bangerAutoCreate: false,
    bangerPlaylistId: null,
    bangerPlaylistProvider: null,
    allowExplicit: true,
    maxSongSeconds: 360,
    perStreamLimit: null,
  }) as const satisfies Record<string, unknown>;

const blockedRow = (id: string, title: string) =>
  ({
    id,
    provider: 'spotify',
    trackUri: `spotify:track:${id}`,
    title,
    reason: null,
    blockedByUserId: null,
    createdAt: '2026-10-01T10:00:00Z',
  }) as const satisfies Record<string, unknown>;

function write(method: string, path: string, options?: Options): Answer {
  calls.push({
    method,
    path,
    blockedTrackId: options?.params?.path?.blockedTrackId,
    body: options?.body,
  });

  if (method === 'PUT') {
    config = { ...config, ...(options?.body as Record<string, unknown>) };
  }

  if (method === 'DELETE') {
    blocked = blocked.filter((row) => row.id !== options?.params?.path?.blockedTrackId);
  }

  if (method === 'POST') {
    const body = options?.body as Record<string, unknown>;

    blocked = [...blocked, { ...blockedRow(String(body.trackUri), String(body.title)), ...body }];
  }

  return method === 'PUT'
    ? ok(config)
    : { response: { status: 200, ok: true } };
}

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => true }),
}));

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => {
      if (path.endsWith('/music/config'))
        return ok(config);

      if (path.endsWith('/music/playlists'))
        return ok(playlists);

      return ok(blocked.map((row) => ({ ...row })));
    },
    PUT: async (path: string, options?: Options) => write('PUT', path, options),
    POST: async (path: string, options?: Options) => write('POST', path, options),
    DELETE: async (path: string, options?: Options) => write('DELETE', path, options),
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  calls.length = 0;
  config = baseConfig();
  blocked = [blockedRow('b1', 'Never Gonna Give You Up'), blockedRow('b2', 'Baby Shark')];
  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

async function open(): Promise<void> {
  mounted = mount(RulesPanel, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        pinia,
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  });

  await settle();
}

const buttonIn = (label: string): HTMLButtonElement =>
  [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
    (b) => b.textContent?.trim() === label || b.getAttribute('aria-label') === label,
  ) as HTMLButtonElement;
const radio = (value: string): HTMLButtonElement =>
  document.body.querySelector<HTMLButtonElement>(
    `[data-slot="radio-group-item"][value="${value}"]`,
  ) as HTMLButtonElement;
const perPerson = (): HTMLInputElement =>
  document.body.querySelector<HTMLInputElement>(
    'input[name="songs-per-person"]',
  ) as HTMLInputElement;
const numberInput = (name: string): HTMLInputElement =>
  document.body.querySelector<HTMLInputElement>(`input[name="${name}"]`) as HTMLInputElement;

const typeInto = async (name: string, value: string): Promise<void> => {
  numberInput(name).value = value;
  numberInput(name).dispatchEvent(new Event('input'));
  await settle();
};

const press = (type: string): PointerEvent =>
  new PointerEvent(type, {
    bubbles: true,
    button: 0,
    pointerType: 'mouse',
  });

// reka-ui opens a select on pointerdown and picks an option on pointerup.
async function pickPlaylist(name: string): Promise<void> {
  Element.prototype.hasPointerCapture = (): boolean => false;
  Element.prototype.setPointerCapture = (): void => undefined;
  Element.prototype.releasePointerCapture = (): void => undefined;

  document.body
    .querySelector<HTMLElement>('[data-setting="banger-playlist"]')
    ?.dispatchEvent(press('pointerdown'));

  await settle();
  const options = [...document.body.querySelectorAll<HTMLElement>('[role="option"]')];

  options.find((item) => item.textContent?.includes(name))?.dispatchEvent(press('pointerup'));
  await settle();
}

const puts = (): Call[] => calls.filter((call) => call.method === 'PUT');

describe('RulesPanel', () => {
  it('shows the saved rules, the blocked songs and what cannot be set yet', async () => {
    await open();

    expect(radio('moderators').getAttribute('aria-checked')).toBe('true');
    expect(radio('everyone').getAttribute('aria-checked')).toBe('false');
    expect(radio('spotify').getAttribute('aria-checked')).toBe('true');
    expect(radio('auto').getAttribute('aria-checked')).toBe('false');
    expect(radio('youtube').getAttribute('aria-checked')).toBe('false');
    expect(perPerson().value).toBe('1');
    const text = document.body.textContent ?? '';

    expect(text).toContain('Never Gonna Give You Up');
    expect(text).toContain('Baby Shark');
    expect(text).toContain('Not available yet');
    expect(numberInput('song-seconds').value).toBe('6:00');
    expect(numberInput('per-stream-limit').value).toBe('0');
    expect(buttonIn('Save').disabled).toBe(true);
  });

  it('saves only the fields that changed', async () => {
    await open();

    radio('subscribers').click();
    await settle();
    expect(buttonIn('Save').disabled).toBe(false);
    buttonIn('Save').click();
    await settle();

    expect(puts()).toHaveLength(1);
    expect(puts()[0]!.body).toEqual({ minTrustLevel: 'subscribers' });
    expect(buttonIn('Save').disabled).toBe(true);
  });

  // Stoney 2026-10-18: "you got to apply the same rules as all other pannels with a toggle in the header bar".
  it('puts the requests-open switch in the panel header and saves it at once, like the other panels', async () => {
    config = { ...baseConfig(), requestsOpen: true };
    await open();

    const header = document.body.querySelector<HTMLElement>('[data-slot="panel-actions"]');
    const row = header?.querySelector<HTMLElement>('[data-labeled-switch]');
    const toggle = row?.querySelector<HTMLButtonElement>('button[role="switch"]');

    expect(header?.innerHTML ?? 'no header').toContain('Requests are open');
    expect(row?.textContent).toContain('Requests are open');

    toggle?.click();
    await settle();

    expect(puts()).toHaveLength(1);
    expect(puts()[0]!.body).toEqual({ requestsOpen: false });
  });

  it('saves the music on/off switch as its own field', async () => {
    await open();

    const toggle = document.body.querySelector<HTMLButtonElement>('[data-setting="music-enabled"]');

    expect(toggle?.getAttribute('aria-checked')).toBe('true');
    toggle?.click();
    await settle();
    buttonIn('Save').click();
    await settle();

    expect(puts()[0]!.body).toEqual({ isEnabled: false });
  });

  it('saves the songs per person the streamer types', async () => {
    await open();

    perPerson().value = '4';
    perPerson().dispatchEvent(new Event('input'));
    await settle();
    buttonIn('Save').click();
    await settle();

    expect(puts()[0]!.body).toEqual({ maxRequestsPerUser: 4 });
  });

  it('saves the queue size and the whole per-role cap map the streamer edits', async () => {
    await open();

    await typeInto('queue-size', '60');
    await typeInto('songs-role-moderator', '5');
    await typeInto('songs-role-vip', '3');
    buttonIn('Save').click();
    await settle();

    expect(puts()).toHaveLength(1);

    expect(puts()[0]!.body).toEqual({
      maxQueueSize: 60,
      maxRequestsPerRole: { vip: 3, moderator: 5 },
    });
  });

  it('saves the sources and the preferred provider', async () => {
    await open();

    document.body.querySelector<HTMLButtonElement>('button[data-setting="allow-youtube"]')?.click();
    radio('youtube').click();
    await settle();
    buttonIn('Save').click();
    await settle();

    expect(puts()[0]!.body).toEqual({ allowYouTube: true, preferredProvider: 'youtube' });
  });

  it('saves the explicit filter, the longest song and the per-stream limit', async () => {
    await open();

    document.body
      .querySelector<HTMLButtonElement>('button[data-setting="allow-explicit"]')
      ?.click();

    await typeInto('song-seconds', '4:30');
    await typeInto('per-stream-limit', '3');
    buttonIn('Save').click();
    await settle();

    expect(puts()).toHaveLength(1);

    expect(puts()[0]!.body).toEqual({
      allowExplicit: false,
      maxSongSeconds: 270,
      perStreamLimit: 3,
    });
  });

  it('turns the per-stream cap off by sending zero', async () => {
    config = { ...baseConfig(), perStreamLimit: 5 };
    await open();

    expect(numberInput('per-stream-limit').value).toBe('5');
    await typeInto('per-stream-limit', '0');
    buttonIn('Save').click();
    await settle();

    expect(puts()[0]!.body).toEqual({ perStreamLimit: 0 });
  });

  it('does not save a song length below ten seconds or a per-stream cap above one hundred', async () => {
    await open();

    await typeInto('song-seconds', '0:05');
    expect(buttonIn('Save').disabled).toBe(true);
    await typeInto('song-seconds', '6:00');
    await typeInto('per-stream-limit', '101');

    expect(buttonIn('Save').disabled).toBe(true);
  });

  it('saves the banger auto-create switch', async () => {
    await open();

    document.body
      .querySelector<HTMLButtonElement>('button[data-setting="banger-auto-create"]')
      ?.click();

    await settle();
    buttonIn('Save').click();
    await settle();

    expect(puts()[0]!.body).toEqual({ bangerAutoCreate: true });
  });

  it('saves the banger playlist with the provider that owns it, and clears it with an empty id', async () => {
    await open();

    await pickPlaylist('Bangers');
    buttonIn('Save').click();
    await settle();

    expect(puts()[0]!.body).toEqual({
      bangerPlaylistId: 'p1',
      bangerPlaylistProvider: 'spotify',
    });

    await pickPlaylist('Choose automatically');
    buttonIn('Save').click();
    await settle();

    expect(puts()[1]!.body).toEqual({ bangerPlaylistId: '', bangerPlaylistProvider: '' });
  });

  it('does not save a role cap above fifty', async () => {
    await open();

    await typeInto('songs-role-vip', '51');

    expect(buttonIn('Save').disabled).toBe(true);
  });

  it('does not save a songs per person below one', async () => {
    await open();

    perPerson().value = '0';
    perPerson().dispatchEvent(new Event('input'));
    await settle();

    expect(buttonIn('Save').disabled).toBe(true);
  });

  it('discards the edits without a request and shows the saved rule again', async () => {
    await open();

    radio('everyone').click();
    await settle();
    expect(radio('everyone').getAttribute('aria-checked')).toBe('true');
    buttonIn('Discard').click();
    await settle();

    expect(calls).toHaveLength(0);
    expect(radio('moderators').getAttribute('aria-checked')).toBe('true');
    expect(buttonIn('Save').disabled).toBe(true);
  });

  it('allows a blocked song with a DELETE and puts it back on Undo with a POST', async () => {
    await open();

    buttonIn('Allow Baby Shark').click();
    await settle();

    expect(calls).toHaveLength(1);
    expect(calls[0]!.method).toBe('DELETE');
    expect(calls[0]!.path.endsWith('/music/blocked-tracks/{blockedTrackId}')).toBe(true);
    expect(calls[0]!.blockedTrackId).toBe('b2');
    expect(document.body.querySelectorAll('[data-blocked-item]')).toHaveLength(1);

    expect(document.body.querySelector('[data-blocked-item]')?.textContent).toContain(
      'Never Gonna Give You Up',
    );

    buttonIn('Undo').click();
    await settle();

    expect(calls[1]!.method).toBe('POST');
    expect(calls[1]!.path.endsWith('/music/blocked-tracks')).toBe(true);

    expect(calls[1]!.body).toEqual({
      provider: 'spotify',
      trackUri: 'spotify:track:b2',
      title: 'Baby Shark',
    });

    expect(document.body.querySelectorAll('[data-blocked-item]')).toHaveLength(2);
  });

  it('says there are no blocked songs when the list is empty', async () => {
    blocked = [];
    await open();

    expect(document.body.textContent).toContain('No songs are blocked.');
  });

  it('shows the not-available reason when the rules are missing', async () => {
    config = null;
    await open();

    expect(document.body.querySelector('[data-slot="panel-empty"]')?.textContent).toContain(
      'Not available yet',
    );
  });
});
