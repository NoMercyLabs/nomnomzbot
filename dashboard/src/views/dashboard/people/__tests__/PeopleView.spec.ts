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

import { onTheWire } from '@/api/__tests__/wire';
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
let listAnswer: (role: string | undefined, pageNumber: number) => Answer = () => page([]);
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

const person = (login: string, over: Record<string, unknown> = {}) =>
  ({
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
  }) as const satisfies Record<string, unknown>;

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
    altPronoun: 'they/them',
    linkedIdentities: [
      {
        provider: 'kick',
        providerUsername: 'alice_k',
        providerDisplayName: 'AliceOnKick',
        providerAvatarUrl: null,
        isPrimary: false,
      },
    ],
    communityStanding: 'Vip',
    subTier: '1000',
    managementRole: 'LeadModerator',
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
    lastWarningAt: '2026-09-04T12:00:00Z',
  },
  trust: { trustScore: 777, heatScore: 888 },
  activity: null,
  streak: {
    currentStreak: 5,
    maxStreak: 11,
    lastSeenDate: '2026-10-12',
  },
  economy: {
    wallet: {
      id: 'w-alice',
      viewerUserId: 'u-alice',
      viewerTwitchUserId: 't-alice',
      viewerDisplayName: 'ALICE',
      viewerAvatarUrl: null,
      balance: 4321,
      lifetimeEarned: 9000,
      lifetimeSpent: 4679,
      isFrozen: true,
      lastActivityAt: null,
    },
    giveawayEntryCount: 6,
    giveawayWinCount: 1,
    leaderboardOptedOut: true,
  },
  permits: {
    activePermits: [],
    ageConsentGranted: null,
    ageConsentConfirmedUtc: null,
  },
  overrides: { shoutoutMessageTemplate: 'Go follow {name}!', ttsVoice: null },
  commandUsage: { totalCommandsUsed: 14, lastUsedUtc: null },
  freeFormData: [{ key: 'favorite game', value: 'Celeste' }],
  recentQuotes: [
    {
      id: 'q1',
      number: 42,
      text: 'never trust a bot',
      quotedDisplayName: 'ALICE',
      contextGame: null,
      quotedAt: null,
      createdAt: '2026-08-01T10:00:00Z',
    },
  ],
  totalQuoteCount: 3,
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
        return listAnswer(
          options?.params?.query?.role as string | undefined,
          Number(options?.params?.query?.Page ?? 1),
        );

      if (path.endsWith('/community/{userId}')) {
        await memberGate;

        const listed = everyone.find((row) => row.id === options?.params?.path?.userId);

        return ok({
          banHistory: [],
          recentActivity: [],
          isVip: false,
          ...(listed ??
            person('dave', {
              id: 't-dave',
              messageCount: 321,
              internalUserId: 'u-dave',
            })),
        });
      }

      if (path.endsWith('/profile'))
        return ok(onTheWire(summary));

      return ok(null);
    },
  },
}));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => true }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1'), current: ref({ login: 'owner' }) }),
}));

const ROW_HEIGHT = 56;
const VIEWPORT_HEIGHT = 600;

let mounted: VueWrapper | null = null;
let pinia: Pinia;
let router: Router;

beforeEach(() => {
  calls.length = 0;
  localStorage.clear();

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
const option = (id: string, label: string, inChannel: boolean) =>
  ({
    id,
    label,
    subLabel: `@${label.toLowerCase()}`,
    imageUrl: null,
    inChannel,
  }) as const satisfies Record<string, unknown>;

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

  // Stoney 2026-10-14: "lazy load all users known to my channel, so i never have to search for someone
  // already known to me".
  it('loads the next page when the list reaches its end, with no button to press', async () => {
    listAnswer = (_role, pageNumber) =>
      pageNumber === 1
        ? {
            ...page(everyone, 4),
            data: {
              data: everyone,
              hasMore: true,
              nextPage: 2,
              total: 4,
            },
          }
        : page([person('dana')], 4);

    await open();

    expect(text()).not.toContain('Load more people');

    mounted!.findComponent({ name: 'VirtualList' }).vm.$emit('end');
    await settle();

    // The everyone list only; the moderator and VIP lists ask for their totals beside it.
    const pages = calls
      .filter((call) => call.path.endsWith('/community') && !call.query.role)
      .map((call) => call.query.Page);

    expect(pages).toEqual([1, 2]);
    expect(rows().at(-1)!.textContent).toContain('DANA');
  });

  it('puts the total of each list on its tab', async () => {
    listAnswer = (role) => {
      if (role === 'moderator')
        return page([everyone[1]!], 4);

      if (role === 'vip')
        return page([everyone[2]!], 2);

      return page(everyone, 120);
    };

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

  // Stoney 2026-10-14: "The full profile view page is the old broken version". The person beside the list is the
  // same profile the dialog shows: header, tabs, the moderation the bot counted, and the actions.
  it('opens the person named in ?u= in the full profile, and reads their profile once', async () => {
    await open('/app/people?u=alice');

    const detail = document.body.querySelector('[data-person-detail]') as HTMLElement;

    expect(profileCalls()).toHaveLength(1);
    expect(profileCalls()[0]!.path).toContain('/community/{userId}/profile');
    expect(detail.querySelector('[data-slot="profile-view"]')).not.toBeNull();
    expect(detail.querySelector('h2')!.textContent).toBe('ALICE');
    expect(detail.textContent).toContain('she/her');
    expect(detail.querySelector('[data-profile-actions]')!.textContent).toMatch(/Timeouts\s*3/);

    expect(detail.querySelector('[data-profile-actions]')!.textContent).toMatch(
      /Messages deleted\s*9/,
    );

    expect(detail.textContent).toContain('Watch streak');
    expect(detail.querySelector('[data-profile-shoutout]')).not.toBeNull();
    expect(detail.querySelector('[data-profile-ban]')).not.toBeNull();
    expect(rows()[0]!.querySelector('button')!.getAttribute('aria-pressed')).toBe('true');
  });

  // The profile dialog's Open full page link carries its tab, so the page opens on that tab.
  it('opens the profile tab named in ?tab=, and keeps a picked tab in the address', async () => {
    await open('/app/people?u=alice&tab=moderation');

    const active = (): string | undefined =>
      document.body
        .querySelector('[data-person-detail] [role="tab"][data-state="active"]')
        ?.textContent?.trim();

    expect(active()).toBe('Moderation');

    const data = Array.from(
      document.body.querySelectorAll<HTMLElement>('[data-person-detail] [role="tab"]'),
    ).find((tab) => tab.textContent?.trim() === 'Data');

    data!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }));
    await flushPromises();

    expect(router.currentRoute.value.query.tab).toBe('data');
    expect(active()).toBe('Data');
  });

  it('opens another person on their Overview, not on the tab of the last one', async () => {
    await open('/app/people?u=alice&tab=moderation');

    rows()[1]!.querySelector('button')!.click();
    await flushPromises();

    expect(router.currentRoute.value.query.u).not.toBe('alice');
    expect(router.currentRoute.value.query.tab).toBeUndefined();
  });

  it('shows every stored fact of the person in words: identity, points and data', async () => {
    await open('/app/people?u=alice');

    const detail = document.body.querySelector('[data-person-detail]') as HTMLElement;
    const points = detail.querySelector('[data-slot="profile-points"]')!.textContent;
    const data = detail.querySelector('[data-slot="profile-data"]')!.textContent;

    expect(detail.querySelector('[data-profile-pronouns]')?.textContent).toBe(
      'she/her · they/them',
    );

    expect(detail.textContent).toMatch(/Other platforms\s*kick: AliceOnKick/);
    expect(detail.textContent).toMatch(/Manages the channel as\s*Lead moderator/);
    expect(detail.textContent).toMatch(/Best watch streak\s*11 streams in a row/);

    expect(detail.querySelector('[data-profile-actions]')!.textContent).toMatch(
      /Last warning\s*Sep 4, 2026/,
    );

    expect(points).toMatch(/Balance\s*4,321/);
    expect(points).toMatch(/Frozen\s*Yes/);
    expect(points).toMatch(/Giveaways won\s*1/);
    expect(points).toMatch(/Leaderboard\s*Hidden by their choice/);
    expect(data).toMatch(/Confirmed 18\+ for games\s*Not asked yet/);
    expect(data).toMatch(/Own shoutout line\s*Go follow \{name\}!/);
    expect(data).toMatch(/Commands used\s*14/);
    expect(data).toMatch(/#42\s*never trust a bot/);
    expect(data).toContain('2 more quotes');
    expect(data).toMatch(/favorite game\s*Celeste/);
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
    expect(document.body.querySelector('[data-person-detail] h2')?.textContent).toBe('BOB');
  });

  it('closes the open profile on Escape and keeps the rest of the address', async () => {
    await open('/app/people?u=alice&view=moderator');
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();

    expect(router.currentRoute.value.query.u).toBeUndefined();
    expect(router.currentRoute.value.query.view).toBe('moderator');
    expect(document.body.querySelector('[data-person-detail] h2')?.textContent).not.toBe('ALICE');
  });

  // Stoney 2026-10-18: "all overlay pannels need a close button". The profile beside the list closes with a button.
  it('closes the open profile with its close button and keeps the rest of the address', async () => {
    await open('/app/people?u=alice&view=moderator');

    const close = document.body.querySelector<HTMLButtonElement>(
      '[data-person-detail] [data-person-close]',
    );

    expect(close?.getAttribute('aria-label')).toBe('Close');
    close!.click();
    await settle();

    expect(router.currentRoute.value.query.u).toBeUndefined();
    expect(router.currentRoute.value.query.view).toBe('moderator');
    expect(document.body.querySelector('[data-person-detail]')).toBeNull();
  });

  it('leaves the profile open when Escape is pressed while typing in the search', async () => {
    await open('/app/people?u=alice');

    const input = document.body.querySelector('input[type="search"]') as HTMLInputElement;

    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();

    expect(router.currentRoute.value.query.u).toBe('alice');
  });

  it('closes the profile on browser Back after a person was picked', async () => {
    await open();
    rows()[1]!.querySelector<HTMLButtonElement>('button')!.click();
    await settle();
    router.back();
    await settle();

    expect(router.currentRoute.value.query.u).toBeUndefined();
    expect(document.body.querySelector('[data-person-detail] h2')?.textContent).not.toBe('BOB');
  });

  it('never reads the profile of a person without an internal id, and still shows what the bot has', async () => {
    await open('/app/people?u=cara');

    expect(profileCalls()).toHaveLength(0);
    expect(document.body.querySelector('[data-person-detail] h2')?.textContent).toBe('CARA');
    expect(text()).toContain('42');
  });

  // Stoney 2026-10-15 (b50c3266b): "standing shows". The moderator profile shows the trust score the bot
  // keeps; the heat score stays internal.
  it('shows the trust score in the standing and never the heat score', async () => {
    await open('/app/people?u=alice');

    expect(text()).toContain('777');
    expect(text()).not.toContain('888');
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
    expect(detail.querySelector('[data-profile-loading]')).not.toBeNull();
    expect(profileCalls()).toHaveLength(0);

    release();
    await settle();

    expect(detail.textContent).toContain('321');
    expect(detail.querySelector('[data-profile-loading]')).toBeNull();
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
    expect(text()).toContain('The bot has no list of Twitch subscribers yet.');
    expect(rows()).toHaveLength(0);

    await router.push('/app/people?view=regular');
    await settle();
    expect(text()).toContain('cannot tell who is a regular');
  });

  // Stoney 2026-10-14: "subscribers and regulars are empty while i have a ton". The bot has no such lists, so the
  // tabs are locked with the reason instead of opening an empty list.
  it('locks the Subscribers and Regulars tabs with the reason and keeps them from opening', async () => {
    await open();

    const subscribers = tab('Twitch subscribers');
    const regulars = tab('Regulars');

    expect(subscribers.hasAttribute('disabled')).toBe(true);
    expect(regulars.hasAttribute('disabled')).toBe(true);
    expect(subscribers.querySelector('[data-slot="tab-lock"]')).not.toBeNull();

    expect(subscribers.parentElement!.getAttribute('title')).toContain(
      'no list of Twitch subscribers',
    );

    expect(regulars.parentElement!.getAttribute('title')).toContain('cannot tell who is a regular');
    expect(tab('Moderators').hasAttribute('disabled')).toBe(false);

    subscribers.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }));
    await settle();

    expect(router.currentRoute.value.query.view).toBeUndefined();
  });

  it('shows the real total in the list header, not the number of rows loaded so far', async () => {
    listAnswer = (role) =>
      role === undefined
        ? page(everyone, 718)
        : page([], 0);

    await open();

    expect(text()).toContain('718 people');
    expect(text()).not.toContain('3 people');
  });

  const sortChoice = (label: string): HTMLElement =>
    [...document.body.querySelectorAll<HTMLElement>('[data-segment]')].find(
      (item) => item.textContent?.trim() === label,
    ) as HTMLElement;
  const firstWords = (): string[] =>
    rows().map((row) => (row.textContent ?? '').slice(2).split('Last seen')[0]!);

  it('sorts A to Z across every page of the channel and remembers the choice', async () => {
    listAnswer = (role, pageNumber) => {
      if (role)
        return page([], 0);

      return pageNumber === 1
        ? {
            data: {
              data: [person('zed'), person('mia')],
              hasMore: true,
              nextPage: 2,
              total: 3,
            },
            response: { status: 200, ok: true },
          }
        : page([person('abe')], 3);
    };

    await open();
    expect(firstWords()).toEqual(['ZED', 'MIA', 'ABE']);

    sortChoice('A to Z').click();
    await settle();

    expect(firstWords()).toEqual(['ABE', 'MIA', 'ZED']);
    expect(localStorage.getItem('nnz.people.sort')).toBe('alphabetical');

    mounted!.unmount();
    mounted = null;
    await open();

    expect(sortChoice('A to Z').getAttribute('data-state')).toBe('on');
    expect(firstWords()[0]).toBe('ABE');
  });

  it('says when the login in ?u= is not in the loaded lists', async () => {
    await open('/app/people?u=nobody');

    expect(text()).toContain('Nobody in the loaded lists has the login "nobody".');
  });
});
