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
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { i18n } from '@/i18n';

import PeopleView from '../PeopleView.vue';
import en from '../i18n/en.json';

type Options = {
  params?: { path?: Record<string, unknown>; query?: Record<string, unknown> };
  body?: unknown;
};
type Call = {
  method: string;
  path: string;
  query: Record<string, unknown>;
};
type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

const calls: Call[] = [];
let listAnswer: (role: string | undefined) => Answer = () => page([]);
let searchAnswer: (q: string) => Answer = () => ok([]);
let lookupAnswer: (login: string) => Answer = () => ok(null);
let memberGate: Promise<void> = Promise.resolve();

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } });
const refused = (): Answer => ({
  error: { status: 'error' },
  response: { status: 403, ok: false },
});
const page = (rows: Record<string, unknown>[], total: number = rows.length): Answer => ({
  data: {
    data: rows,
    hasMore: false,
    nextPage: null,
    total,
  },
  response: { status: 200, ok: true },
});

const person = (login: string, over: Record<string, unknown> = {}): Record<string, unknown> => ({
  id: `t-${login}`,
  internalUserId: `u-${login}`,
  username: login,
  displayName: login.toUpperCase(),
  profileImageUrl: null,
  messageCount: 42,
  watchHours: 3.5,
  commandsUsed: 7,
  trustLevel: 'viewer',
  isBanned: false,
  firstSeen: '2026-01-02T10:00:00Z',
  lastSeen: '2026-10-12T10:00:00Z',
  ...over,
});

const everyone: Record<string, unknown>[] = [
  person('alice'),
  person('bob', { trustLevel: 'moderator' }),
  person('cara', { trustLevel: 'vip', internalUserId: null }),
];

const summary = {
  identity: {
    userId: 'u-alice',
    twitchUserId: 't-alice',
    username: 'alice',
    displayName: 'ALICE',
    profileImageUrl: null,
    pronoun: 'she/her',
    altPronoun: null,
    linkedIdentities: [],
    communityStanding: 'good',
    subTier: '1000',
    managementRole: null,
    memberSinceUtc: null,
    firstSeenUtc: '2026-01-02T10:00:00Z',
  },
  moderationHistory: {
    timeoutCount: 3,
    banCount: 0,
    warningCount: 2,
    messagesDeletedCount: 9,
    lastActionAt: null,
    lastActionType: null,
  },
  trust: { trustScore: 777, heatScore: 888 },
  activity: null,
  streak: {
    currentStreak: 5,
    maxStreak: 11,
    lastSeenDate: '2026-10-12',
  },
};

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options?: Options): Promise<Answer> => {
      calls.push({
        method: 'GET',
        path,
        query: options?.params?.query ?? {},
      });

      if (path.endsWith('/community/search'))
        return searchAnswer(String(options?.params?.query?.q ?? ''));

      if (path.endsWith('/community/lookup'))
        return lookupAnswer(String(options?.params?.query?.login ?? ''));

      if (path.endsWith('/community'))
        return listAnswer(options?.params?.query?.role as string | undefined);

      if (path.endsWith('/community/{userId}')) {
        await memberGate;

        return ok(
          person('dave', {
            id: 't-dave',
            messageCount: 321,
            internalUserId: 'u-dave',
          }),
        );
      }

      if (path.endsWith('/profile'))
        return ok(summary);

      return ok(null);
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

const ROW_HEIGHT = 56;
const VIEWPORT_HEIGHT = 600;

let mounted: VueWrapper | null = null;
let pinia: Pinia;
let router: Router;

beforeEach(() => {
  calls.length = 0;

  listAnswer = (role) => {
    if (role === 'moderator')
      return page([everyone[1]!], 1);

    if (role === 'vip')
      return page([everyone[2]!], 1);

    return page(everyone);
  };

  searchAnswer = () => ok([]);
  lookupAnswer = () => ok(null);
  memberGate = Promise.resolve();
  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);

  // jsdom has no layout: the virtual list gets a viewport and a row height, as in VirtualList.spec.ts.
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    },
  );

  const sizeOf = (element: Element): number =>
    element.hasAttribute('data-virtual-scroll')
      ? VIEWPORT_HEIGHT
      : ROW_HEIGHT;

  vi.spyOn(HTMLElement.prototype, 'offsetHeight', 'get').mockImplementation(function (
    this: HTMLElement,
  ) {
    return sizeOf(this);
  });

  vi.spyOn(HTMLElement.prototype, 'clientHeight', 'get').mockImplementation(function (
    this: HTMLElement,
  ) {
    return sizeOf(this);
  });

  vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockReturnValue(300);

  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (
    this: HTMLElement,
  ) {
    return {
      width: 300,
      height: sizeOf(this),
      top: 0,
      left: 0,
      right: 300,
      bottom: sizeOf(this),
      x: 0,
      y: 0,
    } as DOMRect;
  });
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

async function open(url: string = '/app/people'): Promise<void> {
  const blank = { template: '<div />' };

  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/people',
        name: 'dashboard-people',
        component: blank,
      },
    ],
  });

  await router.push(url);

  mounted = mount(PeopleView, {
    attachTo: document.body,
    global: {
      plugins: [
        i18n,
        pinia,
        router,
        [
          VueQueryPlugin,
          { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
        ],
      ],
    },
  });

  await settle();
}

const text = (): string => document.body.textContent ?? '';
const rows = (): HTMLElement[] => [
  ...document.body.querySelectorAll<HTMLElement>('[data-person-row]'),
];
const tab = (label: string): HTMLElement =>
  [...document.body.querySelectorAll<HTMLElement>('[role="tab"]')].find((t) =>
    t.textContent?.trim().startsWith(label),
  ) as HTMLElement;
const profileCalls = (): Call[] => calls.filter((call) => call.path.endsWith('/profile'));
const DEBOUNCE_WAIT = 400;

const typeSearch = async (value: string): Promise<void> => {
  const input = document.body.querySelector('input[type="search"]') as HTMLInputElement;

  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  await new Promise((done) => setTimeout(done, DEBOUNCE_WAIT));
  await settle();
};

const memberCalls = (): Call[] => calls.filter((call) => call.path.endsWith('/community/{userId}'));

const enter = (): void => {
  const input = document.body.querySelector('input[type="search"]') as HTMLInputElement;

  input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
};

const searchCalls = (): Call[] => calls.filter((call) => call.path.endsWith('/community/search'));
const lookupCalls = (): Call[] => calls.filter((call) => call.path.endsWith('/community/lookup'));
const results = (): HTMLElement[] => [
  ...document.body.querySelectorAll<HTMLElement>('[data-search-result]'),
];
const lookupButton = (): HTMLButtonElement | undefined =>
  [...document.body.querySelectorAll<HTMLButtonElement>('button')].find((b) =>
    b.textContent?.includes('Look up on Twitch'),
  );
const option = (id: string, label: string, inChannel: boolean): Record<string, unknown> => ({
  id,
  label,
  subLabel: `@${label.toLowerCase()}`,
  imageUrl: null,
  inChannel,
});

describe('PeopleView', () => {
  it('lists the people from the bot with the last seen time and a role icon', async () => {
    await open();

    expect(rows().map((row) => row.textContent)).toEqual([
      expect.stringContaining('ALICE'),
      expect.stringContaining('BOB'),
      expect.stringContaining('CARA'),
    ]);

    expect(rows()[0]!.textContent).toContain('Last seen');
    expect(rows()[1]!.querySelector('[aria-label="Moderator"]')).not.toBeNull();
    expect(rows()[2]!.querySelector('[aria-label="VIP"]')).not.toBeNull();
    expect(rows()[0]!.querySelector('[aria-label="Moderator"]')).toBeNull();
    expect(text()).toContain('3 people');
  });

  it('puts the total of each list on its tab', async () => {
    listAnswer = (role) =>
      role === 'moderator'
        ? page([everyone[1]!], 4)
        : role === 'vip'
          ? page([everyone[2]!], 2)
          : page(everyone, 120);

    await open();

    expect(tab('All').textContent).toContain('120');
    expect(tab('Moderators').textContent).toContain('4');
    expect(tab('VIPs').textContent).toContain('2');
  });

  it('asks the bot for the moderators when that tab opens, and lists only them', async () => {
    await open('/app/people?view=moderator');

    expect(
      calls.some((call) => call.path.endsWith('/community') && call.query.role === 'moderator'),
    ).toBe(true);

    expect(rows().map((row) => row.textContent)).toEqual([expect.stringContaining('BOB')]);
    expect(text()).toContain('People who moderate your channel.');
  });

  it('opens the person named in ?u= and reads their profile once', async () => {
    await open('/app/people?u=alice');

    expect(profileCalls()).toHaveLength(1);
    expect(profileCalls()[0]!.path).toContain('/community/{userId}/profile');
    expect(text()).toContain('she/her');
    expect(text()).toContain('Timeouts');
    expect(text()).toContain('Watch streak');
    expect(rows()[0]!.querySelector('button')!.getAttribute('aria-pressed')).toBe('true');
  });

  it('names the subscription tier in words, not the raw Twitch code', async () => {
    await open('/app/people?u=alice');

    expect(text()).toContain('Tier 1');
    expect(text()).not.toContain('1000');
  });

  it('titles the empty detail pane as a profile, not a second People list', async () => {
    await open();

    const headings: string[] = Array.from(document.body.querySelectorAll('h2')).map(
      (h) => h.textContent?.trim() ?? '',
    );

    expect(headings.filter((heading) => heading === 'People')).toHaveLength(1);
    expect(headings).toContain('Profile');
  });

  it('selects a person on a click and writes the login to the address', async () => {
    await open();
    rows()[1]!.querySelector<HTMLButtonElement>('button')!.click();
    await settle();

    expect(router.currentRoute.value.query.u).toBe('bob');
    expect(text()).toContain('@bob');
  });

  it('never reads the profile of a person without an internal id and says why', async () => {
    await open('/app/people?u=cara');

    expect(profileCalls()).toHaveLength(0);
    expect(text()).toContain('The bot has no profile for this person yet.');
    expect(text()).toContain('@cara');
    expect(text()).toContain('42');
  });

  it('never shows the trust or heat score', async () => {
    await open('/app/people?u=alice');

    expect(text()).not.toContain('777');
    expect(text()).not.toContain('888');
  });

  it('tells why the chat lines and activity panels are empty', async () => {
    await open('/app/people?u=alice');

    expect(text()).toContain('The bot cannot list one person');
    expect(text()).toContain('not stored per person yet');
  });

  it('says nobody matches a search that finds no one and offers the Twitch lookup', async () => {
    await open();
    await typeSearch('zzz');

    expect(results()).toHaveLength(0);
    expect(text()).toContain('Nobody matches "zzz".');
    expect(lookupButton()).toBeDefined();
    expect(lookupButton()!.getAttribute('data-variant')).toBe('outline');
  });

  it('searches every known user on the bot, not only the loaded rows, after a pause in typing', async () => {
    searchAnswer = () => ok([option('t-dave', 'DAVE', false), option('t-alice', 'ALICE', true)]);
    await open();
    await typeSearch('dav');

    expect(searchCalls()).toHaveLength(1);
    expect(searchCalls()[0]!.query).toMatchObject({ q: 'dav', scope: 'all' });

    expect(results().map((row) => row.textContent)).toEqual([
      expect.stringContaining('DAVE'),
      expect.stringContaining('ALICE'),
    ]);

    expect(rows()).toHaveLength(0);
  });

  it('marks a result that is not in this channel in plain text, and only that one', async () => {
    searchAnswer = () => ok([option('t-dave', 'DAVE', false), option('t-alice', 'ALICE', true)]);
    await open();
    await typeSearch('a');

    expect(results()[0]!.textContent).toContain('Not in this channel');
    expect(results()[1]!.textContent).not.toContain('Not in this channel');
  });

  it('counts one hit as one person, not one people', async () => {
    searchAnswer = () => ok([option('t-dave', 'DAVE', true)]);
    await open();
    await typeSearch('dav');

    expect(document.body.textContent).toContain('1 person');
    expect(document.body.textContent).not.toContain('1 people');
  });

  it('makes no request for the full person while the name is only being typed', async () => {
    searchAnswer = () => ok([option('t-dave', 'DAVE', false)]);
    await open();
    await typeSearch('dav');

    expect(memberCalls()).toHaveLength(0);
    expect(profileCalls()).toHaveLength(0);
    expect(results()[0]!.textContent).toContain('DAVE');
    expect(results()[0]!.textContent).not.toContain('Last seen');
    expect(document.body.querySelector('[data-person-detail]')).toBeNull();
  });

  it('on Enter shows the seeded name at once and fills in the rest as each part arrives', async () => {
    let release: () => void = () => undefined;

    memberGate = new Promise<void>((done) => {
      release = done;
    });

    searchAnswer = () => ok([option('t-dave', 'DAVE', false)]);
    await open();
    await typeSearch('dav');
    enter();
    await settle();

    expect(router.currentRoute.value.query.u).toBe('dave');
    expect(memberCalls()).toHaveLength(1);
    const detail = document.body.querySelector('[data-person-detail]') as HTMLElement;

    expect(detail.textContent).toContain('DAVE');
    expect(detail.textContent).toContain('@dave');
    expect(detail.textContent).not.toContain('321');
    expect(detail.querySelector('[data-slot="panel-loading"]')).not.toBeNull();
    expect(profileCalls()).toHaveLength(0);

    release();
    await settle();

    expect(detail.textContent).toContain('321');
    expect(detail.querySelector('[data-slot="panel-loading"]')).toBeNull();
    expect(profileCalls()).toHaveLength(1);
  });

  it('opens the same detail when a result is picked with the mouse', async () => {
    searchAnswer = () => ok([option('t-dave', 'DAVE', false)]);
    await open();
    await typeSearch('dav');
    results()[0]!.querySelector<HTMLButtonElement>('button')!.click();
    await settle();

    expect(router.currentRoute.value.query.u).toBe('dave');
    expect(memberCalls()).toHaveLength(1);
  });

  it('looks the typed name up on Twitch only when the button is pressed, then shows that person', async () => {
    memberGate = new Promise<void>(() => undefined);

    lookupAnswer = () =>
      ok({
        id: 't-newbie',
        login: 'newbie',
        displayName: 'Newbie',
        profileImageUrl: null,
        createdAt: '2026-10-01T00:00:00Z',
      });

    await open();
    await typeSearch('newbie');
    expect(lookupCalls()).toHaveLength(0);

    lookupButton()!.click();
    await settle();

    expect(lookupCalls()).toHaveLength(1);
    expect(lookupCalls()[0]!.query).toMatchObject({ login: 'newbie' });
    expect(router.currentRoute.value.query.u).toBe('newbie');
    expect(document.body.querySelector('[data-person-detail]')!.textContent).toContain('Newbie');
  });

  it('says so when Twitch has no such account and shows no person', async () => {
    lookupAnswer = () => ({
      error: { code: 'TWITCH_USER_NOT_FOUND' },
      response: { status: 404, ok: false },
    });

    await open();
    await typeSearch('ghost');
    lookupButton()!.click();
    await settle();

    expect(text()).toContain('Twitch has no account named "ghost".');
    expect(router.currentRoute.value.query.u).toBeUndefined();
  });

  it('shows the tab list again when the search is cleared', async () => {
    await open();
    await typeSearch('zzz');
    await typeSearch('');

    expect(rows()).toHaveLength(3);
    expect(lookupButton()).toBeUndefined();
  });

  it('shows an error with a retry when the bot refuses the list, not an empty list', async () => {
    listAnswer = () => refused();
    await open();

    expect(rows()).toHaveLength(0);

    expect(document.body.querySelector('[data-slot="panel-error"]')?.textContent).toContain(
      'The people list could not load.',
    );
  });

  it('says the list is empty when the bot has seen nobody', async () => {
    listAnswer = () => page([], 0);
    await open();

    expect(document.body.querySelector('[data-slot="panel-empty"]')?.textContent).toContain(
      'has not seen anyone',
    );
  });

  it('gives the Subscribers and Regulars tabs an honest reason instead of a list', async () => {
    await open('/app/people?view=subscriber');
    expect(text()).toContain('The bot has no list of subscribers yet.');
    expect(rows()).toHaveLength(0);

    await router.push('/app/people?view=regular');
    await settle();
    expect(text()).toContain('cannot tell who is a regular');
  });

  it('says when the login in ?u= is not in the loaded lists', async () => {
    await open('/app/people?u=nobody');

    expect(text()).toContain('Nobody in the loaded lists has the login "nobody".');
  });
});
