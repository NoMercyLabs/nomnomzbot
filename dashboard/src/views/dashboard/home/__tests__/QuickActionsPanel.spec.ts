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
import { useSaveErrorsStore } from '@/stores/saveErrors';

import en from '../i18n/en.json';
import QuickActionsPanel from '../components/QuickActionsPanel.vue';

type Call = {
  method: string;
  path: string;
  body?: unknown;
};
type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

const calls: Call[] = [];
let answers: Record<string, () => Answer> = {};

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } });
const refused = (): Answer => ({
  error: { status: 'error' },
  response: { status: 500, ok: false },
});
const nowSeconds = (): number => Math.floor(Date.now() / 1000);

let snoozeCount = 2;
let snoozeFails = false;
let toggleFails = false;
let timers: Record<string, unknown>[] = [];
let startFails = false;
let endFails = false;
let pollList: Record<string, unknown>[] = [];
let suggestions: Record<string, unknown>[] = [];
let shoutoutFails = false;

const suggestion = (
  twitchUserId: string,
  displayName: string,
  streamsLast30Days: number,
  lastCategory: string | null,
) => ({
  twitchUserId,
  login: displayName.toLowerCase(),
  displayName,
  color: '#00FF00',
  lastCategory,
  streamsLast30Days,
  lastShoutoutAt: null,
}) as const satisfies Record<string, unknown>;

const runningPoll = (id: string, votes: number[]) => ({
  id,
  title: 'Which game next?',
  status: 'ACTIVE',
  duration: 300,
  startedAt: new Date().toISOString(),
  endedAt: null,
  channelPointsVotingEnabled: false,
  channelPointsPerVote: 0,
  choices: votes.map((count, index) => ({
    id: `${id}-${index}`,
    title: `Game ${index + 1}`,
    votes: count,
    channelPointsVotes: 0,
    bitsVotes: 0,
  })),
}) as const satisfies Record<string, unknown>;

const startedPoll = (body: Record<string, unknown>) => ({
  ...runningPoll('server-1', []),
  title: body.title,
  duration: body.durationSeconds,
  choices: (body.choices as string[]).map((title, index) => ({
    id: `server-1-${index}`,
    title,
    votes: 0,
    channelPointsVotes: 0,
    bitsVotes: 0,
  })),
}) as const satisfies Record<string, unknown>;

const schedule = () => ({
  snoozeCount,
  snoozeRefreshAt: nowSeconds() + 1172,
  nextAdAt: nowSeconds() + 754,
  duration: 180,
  lastAdAt: 0,
  prerollFreeTime: 0,
}) as const satisfies Record<string, number>;

const timer = (
  name: string,
  intervalMinutes: number,
  isEnabled: boolean,
  firedMinutesAgo: number,
) => ({
  id: name,
  name,
  intervalMinutes,
  isEnabled,
  fireOnce: false,
  lastFiredAt: new Date(Date.now() - firedMinutesAgo * 60_000).toISOString(),
  messageCount: 1,
  createdAt: '2026-01-01T00:00:00Z',
}) as const satisfies Record<string, unknown>;

const queueItem = (position: number, requestedBy: string | null) => ({
  position,
  trackName: `Track ${position}`,
  artist: 'Artist',
  imageUrl: null,
  durationMs: 1000,
  requestedBy,
  cost: 0,
}) as const satisfies Record<string, unknown>;

function freshAnswers() {
  return {
    '/ads/schedule': () => ok(schedule()),
    '/music/queue': () =>
      ok({
        nowPlaying: null,
        queue: [queueItem(1, 'DukaSoft'), queueItem(2, 'DukaSoft'), queueItem(3, 'DukaSoft')],
      }),
    '/timers': () => ok(timers),
    '/live-ops/polls': () => ok(pollList),
    '/live-ops/predictions': () => ok([]),
    '/moderation/shoutout-suggestions': () => ok(onTheWire(suggestions)),
  } as const satisfies Record<string, () => Answer>;
}

function answer(method: string, path: string): Answer {
  calls.push({ method, path });
  const match = Object.keys(answers).find((suffix) => path.endsWith(suffix));

  return match
    ? answers[match]!()
    : ok(null);
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => answer('GET', path),
    PATCH: async (
      path: string,
      options?: { params?: { path?: { pollId?: string } }; body?: unknown },
    ) => {
      calls.push({
        method: 'PATCH',
        path,
        body: options?.body,
      });

      if (endFails)
        return refused();

      pollList = pollList.map((poll) =>
        poll.id === options?.params?.path?.pollId
          ? { ...poll, status: 'TERMINATED' }
          : poll,
      );

      return { data: { data: null }, response: { status: 200, ok: true } };
    },
    POST: async (
      path: string,
      options?: { params?: { path?: { id?: string; twitchUserId?: string } }; body?: unknown },
    ) => {
      const id = options?.params?.path?.id;
      const twitchUserId = options?.params?.path?.twitchUserId;

      calls.push({
        method: 'POST',
        path: id
          ? path.replace('{id}', id)
          : path.replace('{twitchUserId}', twitchUserId ?? '{twitchUserId}'),
        body: options?.body,
      });

      if (path.endsWith('/dismiss')) {
        suggestions = suggestions.filter((entry) => entry.twitchUserId !== twitchUserId);

        return { response: { status: 204, ok: true } };
      }

      if (path.endsWith('/moderation/shoutout')) {
        return shoutoutFails
          ? refused()
          : { data: { data: { path: 'helix' } }, response: { status: 200, ok: true } };
      }

      if (path.endsWith('/live-ops/polls')) {
        if (startFails)
          return refused();

        pollList = [startedPoll(options?.body as Record<string, unknown>), ...pollList];

        return { data: { data: pollList[0] }, response: { status: 201, ok: true } };
      }

      if (path.endsWith('/toggle')) {
        if (toggleFails)
          return refused();

        const target = timers.find((entry) => entry.id === id);

        if (target) {
          target.isEnabled = !target.isEnabled;
        }

        return { data: { data: target }, response: { status: 200, ok: true } };
      }

      if (snoozeFails)
        return refused();

      if (path.endsWith('/ads/snooze')) {
        snoozeCount -= 1;
      }

      return { data: { data: null }, response: { status: 200, ok: true } };
    },
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

// The held action keys; null holds every key.
const access = vi.hoisted(() => ({ held: null as string[] | null }));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({
    known: ref(true),
    can: (key: string): boolean => access.held === null || access.held.includes(key),
  }),
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;
let router: Router;

beforeEach(() => {
  access.held = null;
  calls.length = 0;
  snoozeCount = 2;
  snoozeFails = false;
  toggleFails = false;
  startFails = false;
  endFails = false;
  pollList = [];
  shoutoutFails = false;

  suggestions = [
    suggestion('101', 'JDDoesDev', 12, 'Software and Game Development'),
    suggestion('102', 'MarciPopsis', 9, null),
  ];

  timers = [
    timer('discord', 10, true, 6),
    timer('socials', 30, true, 5),
    timer('lurk', 20, true, 2),
    timer('old', 5, false, 1),
  ];

  answers = freshAnswers();
  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
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
        path: '/app',
        name: 'dashboard-home',
        component: blank,
      },
      {
        path: '/app/music',
        name: 'dashboard-music',
        component: blank,
      },
      {
        path: '/app/stream',
        name: 'dashboard-stream',
        component: blank,
      },
    ],
  });

  await router.push('/app');

  mounted = mount(QuickActionsPanel, {
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

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

const row = (name: string): HTMLElement =>
  document.body.querySelector<HTMLElement>(`[data-quick-row="${name}"]`) as HTMLElement;
const buttonIn = (root: HTMLElement, label: string): HTMLElement =>
  [...root.querySelectorAll<HTMLElement>('button, a')].find(
    (b) => b.textContent?.trim() === label,
  ) as HTMLElement;
const posts = (): Call[] => calls.filter((call) => call.method === 'POST');

describe('QuickActionsPanel gating', () => {
  it('shows no ad, timer or poll row and no action without their keys', async () => {
    access.held = [];

    await open();

    expect(document.querySelector('[data-quick-row="ad"]')).toBeNull();
    expect(document.querySelector('[data-quick-row="timers"]')).toBeNull();
    expect(document.querySelector('[data-quick-row="poll"]')).toBeNull();
    expect(document.querySelector('[data-quick-row="music"]')).not.toBeNull();
  });

  it('shows the rows but no snooze or start buttons with only the read keys', async () => {
    access.held = ['live-ops:ads:read', 'timers:read', 'live-ops:polls:read'];

    await open();

    expect(document.querySelector('[data-quick-row="ad"]')).not.toBeNull();
    expect(document.querySelector('[data-snooze-ad]')).toBeNull();
    expect(document.querySelector('[data-quick-row="poll"]')).not.toBeNull();
    expect(document.querySelector('[data-start-poll]')).toBeNull();
    expect(document.querySelector('[data-start-prediction]')).toBeNull();
  });

  it('shows snooze and the start buttons when the write keys are held', async () => {
    await open();

    expect(document.querySelector('[data-snooze-ad]')).not.toBeNull();
    expect(document.querySelector('[data-start-poll]')).not.toBeNull();
    expect(document.querySelector('[data-start-prediction]')).not.toBeNull();
  });
});

describe('QuickActionsPanel', () => {
  it('shows each row with the values the server gave', async () => {
    await open();

    expect(document.body.textContent).toContain('Quick actions');

    expect(row('ad').querySelector('[data-slot="countdown-time"]')?.textContent).toMatch(
      /^12:\d\d$/,
    );

    expect(row('ad').textContent).toContain('3 min');
    expect(row('ad').textContent).toContain('2 snoozes left');
    expect(row('music').textContent).toContain('3 waiting');
    expect(row('music').textContent).toContain('All sent by DukaSoft');
    expect(row('timers').textContent).toContain('3 timers on');
    expect(row('timers').textContent).toContain('Next timer: discord');
    expect(row('timers').textContent).not.toMatch(/\d:\d\d/);
    expect(row('poll').textContent).toContain('Poll or prediction');
  });

  it('links the song queue to the music page', async () => {
    await open();

    buttonIn(row('music'), 'Open queue').click();
    await settle();

    expect(router.currentRoute.value.name).toBe('dashboard-music');
  });

  it('snoozes once: one call, and the row shows one snooze fewer', async () => {
    await open();

    buttonIn(row('ad'), 'Snooze 5 min').click();
    await settle();

    expect(posts()).toEqual([
      { method: 'POST', path: expect.stringContaining('/live-ops/ads/snooze') },
    ]);

    expect(row('ad').textContent).toContain('1 snooze left');
  });

  it('turns Snooze off when no snooze is left', async () => {
    snoozeCount = 0;
    await open();

    expect(row('ad').textContent).toContain('No snoozes left');
    expect((buttonIn(row('ad'), 'Snooze 5 min') as HTMLButtonElement).disabled).toBe(true);
  });

  it('reports a refused snooze and puts the count back', async () => {
    snoozeFails = true;
    await open();

    buttonIn(row('ad'), 'Snooze 5 min').click();
    await settle();

    expect(useSaveErrorsStore().items).toHaveLength(1);
    expect(row('ad').textContent).toContain('2 snoozes left');
  });

  it('says plainly that no poll or prediction runs, and offers Start a poll and Start a prediction', async () => {
    await open();

    expect(row('poll').textContent).toContain('No poll or prediction running');

    const labels = [...row('poll').querySelectorAll('button, a')].map((b) => b.textContent?.trim());

    expect(labels).toEqual(['Start a poll', 'Start a prediction']);
  });

  it('shows the time left of a running poll', async () => {
    answers['/live-ops/polls'] = () =>
      ok([
        {
          id: 'p1',
          title: 'Which game next?',
          status: 'ACTIVE',
          duration: 300,
          startedAt: new Date().toISOString(),
          endedAt: null,
          choices: [],
        },
        {
          id: 'p0',
          title: 'Old poll',
          status: 'COMPLETED',
          duration: 300,
          startedAt: '2026-01-01T00:00:00Z',
          endedAt: '2026-01-01T00:05:00Z',
          choices: [],
        },
      ]);

    await open();

    expect(row('poll').textContent).toContain('Which game next?');
    expect(row('poll').textContent).not.toContain('Old poll');

    expect(row('poll').querySelector('[data-slot="countdown-time"]')?.textContent).toMatch(
      /^[45]:\d\d$/,
    );

    expect(row('poll').textContent).not.toContain('No poll or prediction running');
  });

  it('shows a locked prediction as locked, with no clock', async () => {
    answers['/live-ops/predictions'] = () =>
      ok([
        {
          id: 'x1',
          title: 'Will it build?',
          status: 'LOCKED',
          predictionWindow: 120,
          createdAt: new Date().toISOString(),
          endedAt: null,
          lockedAt: new Date().toISOString(),
          outcomes: [],
        },
      ]);

    await open();

    expect(row('poll').textContent).toContain('Will it build?');
    expect(row('poll').textContent).toContain('Locked');
    expect(row('poll').querySelector('[data-slot="countdown-time"]')).toBeNull();
  });

  it('shows its own error line for a row whose source failed, and the others still render', async () => {
    answers['/music/queue'] = refused;
    await open();

    expect(row('music').textContent).toContain('The song queue could not load.');
    expect(row('music').textContent).not.toContain('waiting');
    expect(row('ad').textContent).toContain('2 snoozes left');
    expect(row('timers').textContent).toContain('3 timers on');
  });

  it('invents nothing: empty sources say so and show no numbers', async () => {
    answers['/ads/schedule'] = () => ok(null);

    answers['/music/queue'] = () => ok({
      nowPlaying: null,
      queue: [],
      requestsOpen: true,
    });

    answers['/timers'] = () => ok([]);
    await open();

    expect(row('ad').textContent).toContain('No ad break is scheduled.');
    expect(row('ad').querySelector('[data-slot="countdown-time"]')).toBeNull();
    expect(row('music').textContent).toContain('The queue is empty.');
    expect(row('music').textContent).not.toContain('waiting');
    expect(row('timers').textContent).toContain('No timers are on.');
  });

  describe('timers dialog', () => {
    const dialog = (): HTMLElement =>
      document.body.querySelector<HTMLElement>('[role="dialog"]') as HTMLElement;
    const timerRow = (name: string): HTMLElement =>
      document.body.querySelector<HTMLElement>(`[data-timer-row="${name}"]`) as HTMLElement;

    async function openDialog(): Promise<void> {
      await open();
      buttonIn(row('timers'), 'Open timers').click();
      await settle();
    }

    it('lists every timer with its state', async () => {
      await openDialog();

      expect(dialog().textContent).toContain('A paused timer stays saved. It does not fire.');
      expect(timerRow('discord').textContent).toContain('On, every 10 min');
      expect(timerRow('socials').textContent).toContain('On, every 30 min');
      expect(timerRow('old').textContent).toContain('Paused');
      expect(buttonIn(timerRow('discord'), 'Pause')).toBeDefined();
      expect(buttonIn(timerRow('old'), 'Resume')).toBeDefined();
      expect(buttonIn(dialog(), 'Done')).toBeDefined();
    });

    it('pauses an enabled timer: one toggle call for its id, and the row reads Paused', async () => {
      await openDialog();

      buttonIn(timerRow('discord'), 'Pause').click();
      await settle();

      expect(posts()).toEqual([
        { method: 'POST', path: expect.stringContaining('/timers/discord/toggle') },
      ]);

      expect(timerRow('discord').textContent).toContain('Paused');
      expect(timerRow('discord').textContent).not.toContain('On, every');
      expect(buttonIn(timerRow('discord'), 'Resume')).toBeDefined();
      expect(timerRow('socials').textContent).toContain('On, every 30 min');
    });

    it('reports a failed toggle and keeps the old state', async () => {
      toggleFails = true;
      await openDialog();

      buttonIn(timerRow('discord'), 'Pause').click();
      await settle();

      expect(useSaveErrorsStore().items).toHaveLength(1);
      expect(timerRow('discord').textContent).toContain('On, every 10 min');
      expect(buttonIn(timerRow('discord'), 'Pause')).toBeDefined();
    });
  });

  describe('poll dialog', () => {
    const dialog = (): HTMLElement =>
      document.body.querySelector<HTMLElement>('[role="dialog"]') as HTMLElement;
    const field = (name: string): HTMLInputElement =>
      dialog().querySelector<HTMLInputElement>(`[data-poll-field="${name}"]`) as HTMLInputElement;
    const choice = (index: number): HTMLInputElement =>
      dialog().querySelector<HTMLInputElement>(`[data-poll-choice="${index}"]`) as HTMLInputElement;
    const result = (title: string): HTMLElement =>
      dialog().querySelector<HTMLElement>(`[data-poll-result="${title}"]`) as HTMLElement;

    async function type(input: HTMLInputElement, value: string): Promise<void> {
      input.value = value;
      input.dispatchEvent(new Event('input', { bubbles: true }));
      await settle();
    }

    async function click(target: HTMLElement): Promise<void> {
      target.click();
      await settle();
    }

    async function pressDialog(label: string): Promise<void> {
      const target = buttonIn(dialog(), label);

      await click(target);
    }

    async function pressRow(label: string): Promise<void> {
      const target = buttonIn(row('poll'), label);

      await click(target);
    }

    async function openForm(): Promise<void> {
      await open();
      await pressRow('Start a poll');
    }

    async function fillBasics(): Promise<void> {
      await type(field('title'), '  Which game next?  ');
      await type(choice(0), 'Zelda');
      await type(choice(1), 'Metroid');
    }

    it('starts a poll: one POST with the title, the trimmed choices and the duration in seconds', async () => {
      await openForm();
      await fillBasics();
      await type(field('minutes'), '3');
      await pressDialog('Start poll');

      expect(posts()).toEqual([
        {
          method: 'POST',
          path: expect.stringContaining('/live-ops/polls'),
          body: {
            title: 'Which game next?',
            choices: ['Zelda', 'Metroid'],
            durationSeconds: 180,
            channelPointsVotingEnabled: false,
            channelPointsPerVote: 0,
          },
        },
      ]);
    });

    it('adds a third choice and sends it; allows at most five', async () => {
      await openForm();
      await fillBasics();

      for (let added = 0; added < 4; added += 1) {
        const add = buttonIn(dialog(), 'Add choice');

        if (add) {
          await click(add);
        }
      }

      expect(dialog().querySelectorAll('[data-poll-choice]')).toHaveLength(5);
      expect(buttonIn(dialog(), 'Add choice')).toBeUndefined();

      await type(choice(2), 'Hades');
      await pressDialog('Start poll');

      const body = posts()[0]!.body as { choices: string[] };

      expect(body.choices).toEqual(['Zelda', 'Metroid', 'Hades']);
    });

    it('sends the channel points price when channel points voting is on', async () => {
      await openForm();
      await fillBasics();
      await click(dialog().querySelector<HTMLElement>('[data-poll-field="points"]') as HTMLElement);
      await type(field('pointsPerVote'), '50');
      await pressDialog('Start poll');

      expect(posts()[0]!.body).toMatchObject({
        channelPointsVotingEnabled: true,
        channelPointsPerVote: 50,
      });
    });

    it('blocks Start with fewer than two choices and sends nothing', async () => {
      await openForm();
      await type(field('title'), 'Which game next?');
      await type(choice(0), 'Zelda');

      const start = buttonIn(dialog(), 'Start poll') as HTMLButtonElement;

      expect(start.disabled).toBe(true);
      expect(dialog().textContent).toContain('Give at least 2 choices.');

      await click(start);

      expect(posts()).toHaveLength(0);
    });

    it('blocks Start without a title', async () => {
      await openForm();
      await type(choice(0), 'Zelda');
      await type(choice(1), 'Metroid');

      expect((buttonIn(dialog(), 'Start poll') as HTMLButtonElement).disabled).toBe(true);
    });

    it('shows the new poll at once: the row names it and the dialog turns into its results', async () => {
      await openForm();
      await fillBasics();
      await pressDialog('Start poll');

      expect(row('poll').textContent).toContain('Which game next?');
      expect(row('poll').querySelector('[data-slot="countdown-time"]')).not.toBeNull();
      expect(result('Zelda').textContent).toContain('0 votes');
      expect(buttonIn(dialog(), 'End poll')).toBeDefined();
      expect(buttonIn(dialog(), 'Start poll')).toBeUndefined();
    });

    it('reports a refused start and puts the row back to idle', async () => {
      startFails = true;
      await openForm();
      await fillBasics();
      await pressDialog('Start poll');

      expect(useSaveErrorsStore().items).toHaveLength(1);
      expect(row('poll').textContent).toContain('No poll or prediction running');
      expect(choice(0).value).toBe('Zelda');
    });

    it('shows a running poll with each choice, its votes and its share', async () => {
      pollList = [runningPoll('p1', [3, 1, 0])];
      await open();
      await pressRow('Open poll');

      expect(result('Game 1').textContent).toContain('3 votes');
      expect(result('Game 1').textContent).toContain('75%');
      expect(result('Game 2').textContent).toContain('1 vote');
      expect(result('Game 2').textContent).toContain('25%');
      expect(result('Game 3').textContent).toContain('0%');
    });

    it('ends the running poll: one PATCH for its id, and the row goes back to idle', async () => {
      pollList = [runningPoll('p1', [3, 1])];
      await open();
      await pressRow('Open poll');
      await pressDialog('End poll');

      expect(calls.filter((call) => call.method === 'PATCH')).toEqual([
        {
          method: 'PATCH',
          path: expect.stringContaining('/live-ops/polls/{pollId}/end'),
          body: { status: 'TERMINATED' },
        },
      ]);

      expect(row('poll').textContent).not.toContain('Which game next?');
      expect(row('poll').textContent).toContain('No poll or prediction running');
    });

    // Owner 2026-10-15: a finished poll went back to the empty form instead of its result.
    it('keeps the final result open when the poll ends, and offers a new poll from there', async () => {
      pollList = [runningPoll('p1', [3, 1])];
      await open();
      await pressRow('Open poll');
      await pressDialog('End poll');

      expect(dialog().textContent).toContain('Which game next?');
      expect(dialog().textContent).toContain('Final result');
      expect(result('Game 1').textContent).toContain('75%');
      expect(field('title')).toBeNull();
      expect(dialog().textContent).not.toContain('End poll');

      await pressDialog('New poll');

      expect(field('title')).not.toBeNull();
    });

    it('reports a refused end and keeps the poll running', async () => {
      pollList = [runningPoll('p1', [3, 1])];
      endFails = true;
      await open();
      await pressRow('Open poll');
      await pressDialog('End poll');

      expect(useSaveErrorsStore().items).toHaveLength(1);
      expect(row('poll').textContent).toContain('Which game next?');
      expect(buttonIn(dialog(), 'End poll')).toBeDefined();
    });
  });

  // Owner 2026-10-12: chatters in this stream who went live often, most streams first.
  describe('suggested shoutouts', () => {
    const group = (): HTMLElement =>
      document.body.querySelector('[data-shoutout-suggestions]') as HTMLElement;
    const suggestionRow = (userId: string): HTMLElement =>
      group().querySelector(`[data-suggestion="${userId}"]`) as HTMLElement;

    it('lists each chatter with their streams and category, and counts them in the header', async () => {
      await open();

      expect(group().querySelector('[data-suggestions-header]')?.textContent).toContain(
        'Suggested shoutouts · 2',
      );

      expect(suggestionRow('101').textContent).toContain('JDDoesDev');

      expect(suggestionRow('101').textContent).toContain(
        '12 streams in 30 days · Software and Game Development',
      );

      const avatar = suggestionRow('101').querySelector<HTMLElement>('[data-chat-color]');

      expect(avatar?.style.backgroundColor).toBe('rgb(0, 255, 0)');

      expect(suggestionRow('102').textContent).toContain('9 streams in 30 days');
      expect(suggestionRow('102').textContent).not.toContain('·');
    });

    it('sends the shoutout for that chatter and marks the row sent', async () => {
      await open();
      buttonIn(suggestionRow('101'), 'Shoutout').click();
      await settle();

      expect(posts()).toEqual([
        {
          method: 'POST',
          path: expect.stringContaining('/moderation/shoutout'),
          body: { targetTwitchUserId: '101' },
        },
      ]);

      expect(suggestionRow('101').textContent).toContain('Shoutout sent');
      expect(buttonIn(suggestionRow('101'), 'Shoutout')).toBeUndefined();

      expect(group().querySelector('[data-suggestions-header]')?.textContent).toContain(
        'Suggested shoutouts · 1',
      );
    });

    it('says a refused shoutout failed and keeps the button', async () => {
      shoutoutFails = true;
      await open();
      buttonIn(suggestionRow('101'), 'Shoutout').click();
      await settle();

      expect(suggestionRow('101').textContent).toContain('The shoutout did not go out.');
      expect(buttonIn(suggestionRow('101'), 'Shoutout')).toBeDefined();
    });

    it('drops a chatter with Not now, through the dismiss call', async () => {
      await open();
      buttonIn(suggestionRow('102'), 'Not now').click();
      await settle();

      expect(posts()[0]!.path).toContain('/shoutout-suggestions/102/dismiss');
      expect(suggestionRow('102')).toBeNull();

      expect(group().querySelector('[data-suggestions-header]')?.textContent).toContain(
        'Suggested shoutouts · 1',
      );
    });

    it('shows no group when there is no one to suggest', async () => {
      suggestions = [];
      await open();

      expect(group()).toBeNull();
    });
  });
});
