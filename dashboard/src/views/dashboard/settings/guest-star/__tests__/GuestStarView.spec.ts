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

import { i18n } from '@/i18n';

import GuestStarView from '../GuestStarView.vue';
import en from '../i18n/en.json';

type Media = {
  isHostEnabled: boolean;
  isGuestEnabled: boolean;
  isAvailable: boolean;
};
type Guest = {
  slotId: string;
  isLive: boolean;
  userId: string;
  userDisplayName: string;
  userLogin: string;
  volume: number;
  assignedAt: string;
  audioSettings: Media;
  videoSettings: Media;
};
type Invite = {
  userId: string;
  invitedAt: string;
  status: string;
};
type Call = {
  method: string;
  path: string;
  params: Record<string, string>;
  query?: Record<string, string | boolean>;
  body?: Record<string, unknown>;
};
type Init = {
  params: { path: Record<string, string>; query?: Record<string, string | boolean> };
  body?: Record<string, unknown>;
};
type Answer = {
  data?: unknown;
  error?: unknown;
  response: { ok: boolean; status: number };
};

const calls = vi.hoisted(() => [] as Call[]);
const access = vi.hoisted(() => ({ write: true }));
const refusal = vi.hoisted(() => ({ next: null as { status: number; code: string } | null }));
const toasts = vi.hoisted(() => [] as string[]);
const world = vi.hoisted(() => ({
  settings: {} as Record<string, unknown>,
  session: null as { id: string; guests: Guest[] } | null,
  invites: [] as Invite[],
}));

const base = '/api/v{version}/channels/{channelId}/guest-star';

const media = (): Media => ({
  isHostEnabled: true,
  isGuestEnabled: true,
  isAvailable: true,
});
const guest = (slotId: string, userId: string, name: string): Guest => ({
  slotId,
  isLive: false,
  userId,
  userDisplayName: name,
  userLogin: name.toLowerCase(),
  volume: 50,
  assignedAt: '2026-11-18T10:00:00Z',
  audioSettings: media(),
  videoSettings: media(),
});

const newcomer = (body: Record<string, unknown>): Guest =>
  guest(String(body.slotId), String(body.guestTwitchUserId), 'Newcomer');

const ok = (data: unknown): Answer => ({ data: { status: 'ok', data }, response: { ok: true, status: 200 } });
const none = (): Answer => ok(null);

function serve(method: string, path: string, init: Init): Answer {
  const slot = init.params.path.slotId;
  const target = world.session?.guests.find((row) => row.slotId === slot);

  switch (`${method} ${path.replace(base, '')}`) {
    case 'GET /settings':
      return ok(world.settings);
    case 'PUT /settings':
      world.settings = { ...world.settings, ...init.body };

      return none();
    case 'GET /session':
      return world.session === null
        ? { error: { code: 'not_found' }, response: { ok: false, status: 404 } }
        : ok(structuredClone(world.session));
    case 'POST /session':
      world.session = { id: 's1', guests: [] };

      return ok(world.session);

    case 'DELETE /sessions/{sessionId}': {
      const ended = world.session;

      world.session = null;

      return ok(ended);
    }

    case 'GET /sessions/{sessionId}/invites':
      return ok(world.invites);
    case 'POST /sessions/{sessionId}/invites':
      world.invites.push({
        userId: String(init.body?.guestTwitchUserId),
        invitedAt: '2026-11-18T10:00:00Z',
        status: 'invited',
      });

      return none();
    case 'DELETE /sessions/{sessionId}/invites/{guestTwitchUserId}':
      world.invites = world.invites.filter(
        (row) => row.userId !== init.params.path.guestTwitchUserId,
      );

      return none();
    case 'POST /sessions/{sessionId}/slots':
      world.session?.guests.push(newcomer(init.body ?? {}));

      return none();
    case 'PATCH /sessions/{sessionId}/slots/{slotId}':
      if (target !== undefined) {
        target.slotId = String(init.body?.destinationSlotId ?? '9');
      }

      return none();
    case 'DELETE /sessions/{sessionId}/slots/{slotId}':
      if (world.session !== null) {
        world.session.guests = world.session.guests.filter((row) => row.slotId !== slot);
      }

      return none();
    case 'PATCH /sessions/{sessionId}/slots/{slotId}/settings':
      if (target !== undefined) {
        applySlotSettings(target, init.body ?? {});
      }

      return none();
    default:
      return { error: { code: 'other' }, response: { ok: false, status: 500 } };
  }
}

function applySlotSettings(target: Guest, body: Record<string, unknown>): void {
  if (typeof body.isAudioEnabled === 'boolean') {
    target.audioSettings.isHostEnabled = body.isAudioEnabled;
  }

  if (typeof body.isVideoEnabled === 'boolean') {
    target.videoSettings.isHostEnabled = body.isVideoEnabled;
  }

  if (typeof body.isLive === 'boolean') {
    target.isLive = body.isLive;
  }

  if (typeof body.volume === 'number') {
    target.volume = body.volume;
  }
}

function record(method: string) {
  return async (path: string, init: Init): Promise<Answer> => {
    calls.push({
      method,
      path: path.replace(base, ''),
      params: init.params.path,
      query: init.params.query,
      body: init.body,
    });

    if (method !== 'GET' && refusal.next !== null) {
      const { status, code } = refusal.next;

      return { error: {
        status: 'error',
        code,
        message: code,
      }, response: { ok: false, status } };
    }

    return serve(method, path, init);
  };
}

vi.mock('@/api/client', () => ({
  client: {
    use: (): undefined => undefined,
    GET: record('GET'),
    PUT: record('PUT'),
    POST: record('POST'),
    PATCH: record('PATCH'),
    DELETE: record('DELETE'),
  },
}));

vi.mock('vue-sonner', () => ({
  toast: {
    error: (message: string): number => toasts.push(message),
    success: (): number => 0,
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: computed(() => 'c1') }),
}));

vi.mock('@/composables/useCan', () => ({
  useCan: () => ({ can: (key: string) => key !== 'guest-star:write' || access.write }),
}));

const q = <T extends Element>(selector: string): T => document.body.querySelector<T>(selector) as T;
const exists = (selector: string): boolean => document.body.querySelector(selector) !== null;
const called = (method: string, path: string): Call[] =>
  calls.filter((row) => row.method === method && row.path === path);

async function click(element: Element): Promise<void> {
  element.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
  await flushPromises();
}

async function typeInto(selector: string, value: string, event = 'input'): Promise<void> {
  const field = q<HTMLInputElement>(selector);

  field.value = value;
  field.dispatchEvent(new Event(event, { bubbles: true }));
  await flushPromises();
}

async function mountView(): Promise<VueWrapper> {
  i18n.global.mergeLocaleMessage('en', en);
  const wrapper = mount(GuestStarView, {
    attachTo: document.body,
    global: {
      plugins: [createPinia(), [VueQueryPlugin, { queryClient: new QueryClient() }], i18n],
      stubs: { SettingsPageFrame: { template: '<div><slot name="actions" /><slot /></div>' } },
    },
  });

  await flushPromises();

  return wrapper;
}

const words = en.dashboard.settings.guestStar;

beforeEach(() => {
  calls.length = 0;
  toasts.length = 0;
  access.write = true;
  refusal.next = null;

  world.settings = {
    isModeratorSendLiveEnabled: true,
    slotCount: 6,
    isBrowserSourceAudioEnabled: true,
    // What live Twitch sends on read (2026-11-19): the short name, not the *_LAYOUT name the write takes.
    groupLayout: 'TILED',
    browserSourceToken: 'placeholder-token',
  };

  world.session = null;
  world.invites = [];
});

afterEach(() => {
  document.body.innerHTML = '';
});

describe('the guest star settings form', () => {
  it('shows the values the channel holds, read from the settings endpoint', async () => {
    await mountView();

    expect(called('GET', '/settings')).toHaveLength(1);
    expect(q<HTMLInputElement>('[data-setting="slotCount"]').value).toBe('6');
    expect(q('[data-setting="moderatorSendLive"]').getAttribute('aria-checked')).toBe('true');
  });

  // Owner 2026-11-19: Save sticks to the bottom of the scroll area and is primary only while a change waits.
  it('keeps Save in a sticky footer and turns it primary only while a change waits', async () => {
    await mountView();
    const footer = q('[data-save-settings]').closest('footer');

    expect(footer?.classList.contains('sticky')).toBe(true);
    expect(footer?.classList.contains('bottom-0')).toBe(true);
    expect(q('[data-save-settings]').getAttribute('data-variant')).toBe('outline');

    await typeInto('[data-setting="slotCount"]', '4');

    expect(q('[data-save-settings]').getAttribute('data-variant')).toBe('default');
  });

  it('saves the edited values with one PUT and shows the saved state afterwards', async () => {
    await mountView();

    await typeInto('[data-setting="slotCount"]', '4');
    await click(q('[data-setting="moderatorSendLive"]'));
    await click(q('[data-save-settings]'));

    expect(called('PUT', '/settings').map((row) => row.body)).toEqual([
      {
        isModeratorSendLiveEnabled: false,
        slotCount: 4,
        isBrowserSourceAudioEnabled: true,
        groupLayout: 'TILED_LAYOUT',
        regenerateBrowserSources: false,
      },
    ]);

    expect(q<HTMLInputElement>('[data-setting="slotCount"]').value).toBe('4');
    expect(q('[data-save-settings]').hasAttribute('disabled')).toBe(true);
  });

  it('asks for new browser source links only when the switch is on', async () => {
    await mountView();

    await click(q('[data-setting="regenerate"]'));
    await click(q('[data-save-settings]'));

    expect(called('PUT', '/settings')[0]?.body?.regenerateBrowserSources).toBe(true);
  });
});

describe('the guest star session card', () => {
  it('offers only Start when no session runs, and hides invites and slots', async () => {
    await mountView();

    expect(exists('[data-session-start]')).toBe(true);
    expect(exists('[data-session-end]')).toBe(false);
    expect(exists('[data-panel="invites"]')).toBe(false);
    expect(exists('[data-panel="slots"]')).toBe(false);
    expect(document.body.textContent).toContain(words.session.empty);
  });

  it('starts a session with a POST and then shows the running session with its panels', async () => {
    await mountView();

    await click(q('[data-session-start]'));

    expect(called('POST', '/session')).toHaveLength(1);
    expect(exists('[data-session-end]')).toBe(true);
    expect(exists('[data-session-start]')).toBe(false);
    expect(exists('[data-panel="invites"]')).toBe(true);
    expect(exists('[data-panel="slots"]')).toBe(true);
  });

  it('ends the session by its id after a confirm, and the page goes back to Start', async () => {
    world.session = { id: 's1', guests: [] };
    await mountView();

    await click(q('[data-session-end]'));

    expect(called('DELETE', '/sessions/{sessionId}')).toHaveLength(0);

    await click(q('[data-confirm-action]'));

    expect(called('DELETE', '/sessions/{sessionId}').map((row) => row.params.sessionId)).toEqual(['s1']);
    expect(exists('[data-session-start]')).toBe(true);
  });
});

describe('the guest star invites', () => {
  beforeEach(() => {
    world.session = { id: 's1', guests: [] };

    world.invites = [{
      userId: '1001',
      invitedAt: '2026-11-18T10:00:00Z',
      status: 'invited',
    }];
  });

  it('lists the invites of the running session', async () => {
    await mountView();

    expect(called('GET', '/sessions/{sessionId}/invites')[0]?.params.sessionId).toBe('s1');
    expect(exists('[data-invite="1001"]')).toBe(true);
  });

  it('invites a person by Twitch user id from a dialog, and the invite appears in the list', async () => {
    await mountView();

    await click(q('[data-invite-open]'));
    await typeInto('[data-field="guestTwitchUserId"]', '2002');
    await click(q('[data-form-action]'));

    expect(called('POST', '/sessions/{sessionId}/invites').map((row) => row.body)).toEqual([
      { guestTwitchUserId: '2002' },
    ]);

    expect(exists('[data-invite="2002"]')).toBe(true);
  });

  it('removes an invite by the id of the invited person, and the row leaves the list', async () => {
    await mountView();

    await click(q('[data-invite-remove="1001"]'));

    expect(called('DELETE', '/sessions/{sessionId}/invites/{guestTwitchUserId}').map((row) => row.params)).toEqual([
      {
        version: '1',
        channelId: 'c1',
        sessionId: 's1',
        guestTwitchUserId: '1001',
      },
    ]);

    expect(exists('[data-invite="1001"]')).toBe(false);
  });
});

describe('the guest star slots', () => {
  beforeEach(() => {
    world.session = { id: 's1', guests: [guest('1', '3003', 'Alice')] };
  });

  it('shows each guest by display name in the slot they hold', async () => {
    await mountView();

    expect(q('[data-guest="1"]').textContent).toContain('Alice');
    expect(q('[data-guest="1"]').textContent).not.toContain('3003');
  });

  it('assigns a guest to a slot from a dialog, and the guest appears in that slot', async () => {
    await mountView();

    await click(q('[data-assign-open]'));
    await typeInto('[data-field="guestTwitchUserId"]', '4004');
    await typeInto('[data-field="slotId"]', '2');
    await click(q('[data-form-action]'));

    expect(called('POST', '/sessions/{sessionId}/slots').map((row) => row.body)).toEqual([
      { guestTwitchUserId: '4004', slotId: '2' },
    ]);

    expect(exists('[data-guest="2"]')).toBe(true);
  });

  it('moves a guest to a destination slot, and the guest shows in the new slot', async () => {
    await mountView();

    await click(q('[data-guest-move="1"]'));
    await typeInto('[data-field="destinationSlotId"]', '3');
    await click(q('[data-form-action]'));

    expect(called('PATCH', '/sessions/{sessionId}/slots/{slotId}').map((row) => [row.params.slotId, row.body])).toEqual([
      ['1', { destinationSlotId: '3' }],
    ]);

    expect(exists('[data-guest="3"]')).toBe(true);
    expect(exists('[data-guest="1"]')).toBe(false);
  });

  it('sends a null destination when the move has no slot, which empties the interface', async () => {
    await mountView();

    await click(q('[data-guest-move="1"]'));
    await click(q('[data-form-action]'));

    expect(called('PATCH', '/sessions/{sessionId}/slots/{slotId}')[0]?.body).toEqual({ destinationSlotId: null });
  });

  it('removes a guest with the guest id in the query, without a reinvite by default', async () => {
    await mountView();

    await click(q('[data-guest-remove="1"]'));
    await click(q('[data-confirm-action]'));

    expect(called('DELETE', '/sessions/{sessionId}/slots/{slotId}').map((row) => [row.params.slotId, row.query])).toEqual([
      ['1', { guestTwitchUserId: '3003', shouldReinviteGuest: false }],
    ]);

    expect(exists('[data-guest="1"]')).toBe(false);
  });

  it('removes a guest and sends them back to the waiting room when the reinvite switch is on', async () => {
    await mountView();

    await click(q('[data-guest-remove="1"]'));
    await click(q('[data-setting="reinvite"]'));
    await click(q('[data-confirm-action]'));

    expect(called('DELETE', '/sessions/{sessionId}/slots/{slotId}')[0]?.query).toEqual({
      guestTwitchUserId: '3003',
      shouldReinviteGuest: true,
    });
  });

  it('mutes the audio of one slot with a PATCH and the switch shows the new state', async () => {
    await mountView();

    await click(q('[data-guest-audio="1"]'));

    expect(called('PATCH', '/sessions/{sessionId}/slots/{slotId}/settings').map((row) => row.body)).toEqual([
      { isAudioEnabled: false },
    ]);

    expect(q('[data-guest-audio="1"]').getAttribute('aria-checked')).toBe('false');
  });

  it('turns the video of one slot off with a PATCH and the switch shows the new state', async () => {
    await mountView();

    await click(q('[data-guest-video="1"]'));

    expect(called('PATCH', '/sessions/{sessionId}/slots/{slotId}/settings')[0]?.body).toEqual({ isVideoEnabled: false });
    expect(q('[data-guest-video="1"]').getAttribute('aria-checked')).toBe('false');
  });

  it('puts a guest live with a PATCH and the switch shows the new state', async () => {
    await mountView();

    await click(q('[data-guest-live="1"]'));

    expect(called('PATCH', '/sessions/{sessionId}/slots/{slotId}/settings')[0]?.body).toEqual({ isLive: true });
    expect(q('[data-guest-live="1"]').getAttribute('aria-checked')).toBe('true');
  });

  it('sets the volume of one slot with a PATCH and the field shows the new volume', async () => {
    await mountView();

    await typeInto('[data-guest-volume="1"]', '80', 'change');

    expect(called('PATCH', '/sessions/{sessionId}/slots/{slotId}/settings')[0]?.body).toEqual({ volume: 80 });
    expect(q<HTMLInputElement>('[data-guest-volume="1"]').value).toBe('80');
  });
});

describe('the guest star errors', () => {
  it.each([
    [403, 'missing_scope'],
    [409, 'no_token'],
    [404, 'not_found'],
    [429, 'rate_limited'],
  ] as const)('says in plain words what an API %s %s means', async (status, code) => {
    await mountView();
    refusal.next = { status, code };

    await click(q('[data-session-start]'));

    expect(toasts).toEqual([words.errors[code]]);
  });
});

describe('the guest star page for a person who may only read', () => {
  it('shows the running session and no control that writes', async () => {
    access.write = false;
    world.session = { id: 's1', guests: [guest('1', '3003', 'Alice')] };

    world.invites = [{
      userId: '1001',
      invitedAt: '2026-11-18T10:00:00Z',
      status: 'invited',
    }];

    await mountView();

    expect(q('[data-guest="1"]').textContent).toContain('Alice');

    const writeControls = [
      '[data-save-settings]',
      '[data-session-start]',
      '[data-session-end]',
      '[data-invite-open]',
      '[data-invite-remove]',
      '[data-assign-open]',
      '[data-guest-move]',
      '[data-guest-remove]',
    ];

    expect(writeControls.filter(exists)).toEqual([]);
  });

  it('shows held values as text, never as a disabled field with no reason', async () => {
    access.write = false;
    world.session = { id: 's1', guests: [guest('1', '3003', 'Alice')] };

    await mountView();

    expect(document.querySelectorAll('input:disabled, button:disabled[role="combobox"]')).toHaveLength(0);
    expect(q('[data-setting="slotCount"]').textContent?.trim()).toBe('6');

    expect(q('[data-setting="groupLayout"]').textContent?.trim()).toBe(
      i18n.global.t('dashboard.settings.guestStar.settings.layouts.TILED_LAYOUT'),
    );

    expect(q('[data-guest-volume="1"]').textContent?.trim()).toBe('50');
  });
});
