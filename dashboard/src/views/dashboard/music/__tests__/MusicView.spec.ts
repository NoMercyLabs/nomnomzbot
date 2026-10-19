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

import MusicView from '../MusicView.vue';
import en from '../i18n/en.json';

type Options = { params?: { path?: Record<string, unknown> }; body?: unknown };
type Call = {
  method: string;
  path: string;
  position: unknown;
  body: unknown;
};
type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};

const calls: Call[] = [];
let answers: Record<string, () => Answer> = {};
let queue: Record<string, unknown>[] = [];
let nowPlaying: Record<string, unknown> | null = null;
let requestsOpen = true;
let heldActionKeys: string[] = ['music:queue:moderate'];

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } });
const refused = (): Answer => ({
  error: { status: 'error' },
  response: { status: 403, ok: false },
});

const track = (
  position: number,
  trackName: string,
  artist: string,
  requestedBy: string,
) => ({
  position,
  number: position,
  trackName,
  artist,
  imageUrl: null,
  durationMs: 200000,
  requestedBy,
  cost: 0,
}) as const satisfies Record<string, unknown>;

const playing = (overrides: Record<string, unknown> = {}) => ({
  trackName: 'Midnight City',
  artist: 'M83',
  album: 'Hurry Up',
  imageUrl: null,
  durationMs: 240000,
  progressMs: 60000,
  isPlaying: true,
  volume: 40,
  requestedBy: 'DukaSoft',
  provider: 'spotify',
  shuffleState: false,
  repeatState: 'off',
  canSetShuffle: true,
  canSetRepeat: true,
  canSkipNext: true,
  canSkipPrevious: true,
  canSeek: true,
  canPause: true,
  canResume: true,
  ...overrides,
}) as const satisfies Record<string, unknown>;

function freshAnswers() {
  return {
    '/roles/effective/me': () => ok({ heldActionKeys }),
    '/music/queue': () =>
      ok({
        nowPlaying,
        queue: queue.map((item) => ({ ...item })),
        requestsOpen,
      }),
    '/music/history': () =>
      ok([
        {
          trackName: 'Strobe',
          artist: 'deadmau5',
          imageUrl: null,
          provider: 'spotify',
          requesterId: '100',
          requesterName: 'DukaSoft',
          requestedAt: '2026-10-01T12:00:00Z',
        },
        {
          trackName: '',
          artist: '',
          imageUrl: null,
          provider: 'spotify',
          requesterId: '200',
          requesterName: 'Bamo16',
          requestedAt: '2026-09-30T12:00:00Z',
        },
      ]),
    '/music/top-requesters': () =>
      ok([
        {
          userId: '100',
          displayName: 'DukaSoft',
          count: 266,
        },
        {
          userId: '200',
          displayName: 'Bamo16',
          count: 133,
        },
      ]),
    '/integrations/status': () =>
      ok([
        {
          provider: 'spotify',
          connected: true,
          accountName: 'stoney',
          needsReauth: false,
          grantedScopeSets: [],
          capabilities: {},
          loginOnly: false,
        },
      ]),
  } as const satisfies Record<string, () => Answer>;
}

function read(path: string): Answer {
  const match = Object.keys(answers).find((suffix) => path.endsWith(suffix));

  return match
    ? answers[match]!()
    : ok(null);
}

// The bot's move: the song at `position` lands at `toPosition`, and the positions stay in queue order.
function moved(rows: Record<string, unknown>[], options?: Options): Record<string, unknown>[] {
  const from = rows.findIndex((item) => item.position === options?.params?.path?.position);
  const target = (options?.body as { toPosition?: number } | undefined)?.toPosition;
  const to = rows.findIndex((item) => item.position === target);
  const rest = rows.filter((_, index) => index !== from);

  rest.splice(to, 0, rows[from]!);

  return rest.map((item, index) => ({ ...item, position: index + 1 }));
}

// The page hands its drop handler to Sortable; the test drops a row by calling it.
let sortable: { onUpdate?: (event: { oldIndex?: number; newIndex?: number }) => void } = {};

vi.mock('@vueuse/integrations/useSortable', () => ({
  useSortable: (_target: unknown, _list: unknown, options: typeof sortable) => {
    sortable = options;
  },
}));

function write(method: string, path: string, options?: Options): Answer {
  calls.push({
    method,
    path,
    position: options?.params?.path?.position,
    body: options?.body,
  });

  if (method === 'DELETE') {
    queue = queue.filter((item) => item.position !== options?.params?.path?.position);
  }

  if (path.endsWith('/ban')) {
    queue = queue.filter((item) => item.position !== options?.params?.path?.position);
  }

  if (path.endsWith('/move')) {
    queue = moved(queue, options);
  }

  if (path.endsWith('/requests-open')) {
    requestsOpen = (options!.body as { open: boolean }).open;
  }

  return { response: { status: 200, ok: true } };
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => read(path),
    POST: async (path: string, options?: Options) => write('POST', path, options),
    DELETE: async (path: string, options?: Options) => write('DELETE', path, options),
    PUT: async (path: string, options?: Options) => write('PUT', path, options),
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  calls.length = 0;

  queue = [
    track(1, 'Strobe', 'deadmau5', 'sample_viewer'),
    track(2, 'Opus', 'Eric Prydz', 'DukaSoft'),
  ];

  nowPlaying = playing();
  requestsOpen = true;
  heldActionKeys = ['music:queue:moderate', 'music:config:read'];
  answers = freshAnswers();
  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);

  // jsdom has no layout; the seek and volume sliders measure their track with this.
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

async function open(): Promise<void> {
  const blank = { template: '<div />' };
  const router: Router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/music',
        name: 'dashboard-music',
        component: blank,
      },
      {
        path: '/app/rewards',
        name: 'dashboard-rewards',
        component: blank,
      },
    ],
  });

  await router.push('/app/music');

  mounted = mount(MusicView, {
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

const panel = (title: string): HTMLElement =>
  [...document.body.querySelectorAll<HTMLElement>('[data-slot="panel-card"]')].find((card) =>
    card.querySelector('h2')?.textContent?.includes(title),
  ) as HTMLElement;

// Drops a row the way Sortable does: it moves the node itself, then reports the indexes.
function drop(oldIndex: number, newIndex: number): void {
  const list = panel('Request queue').querySelector('[data-queue-list]') as HTMLElement;
  const item = list.children[oldIndex] as HTMLElement;

  list.insertBefore(item, list.children[newIndex + (newIndex > oldIndex
    ? 1
    : 0)] ?? null);

  sortable.onUpdate?.({
    oldIndex,
    newIndex,
    item,
    from: list,
  } as never);
}

const buttonIn = (root: HTMLElement, label: string): HTMLButtonElement =>
  [...root.querySelectorAll<HTMLButtonElement>('button')].find(
    (b) => b.textContent?.trim() === label || b.getAttribute('aria-label') === label,
  ) as HTMLButtonElement;
const writes = (): Call[] => calls;

describe('MusicView history panels', () => {
  it('lists the stored requests with a profile-opening requester name', async () => {
    await open();

    const rows = [...panel('History').querySelectorAll<HTMLElement>('[data-history-item]')];

    expect(rows).toHaveLength(2);
    expect(rows[0]!.textContent).toContain('Strobe');
    expect(rows[0]!.textContent).toContain('deadmau5');

    expect(rows[0]!.querySelector('[data-slot="user-name"]')?.getAttribute('data-user')).toBe(
      '100',
    );

    expect(rows[1]!.textContent).toContain(en.dashboard.music.history.unknownTrack);
  });

  it('lists the top requesters with their counts and a bar scaled to the leader', async () => {
    await open();

    const rows = [
      ...panel('Thanks for the music').querySelectorAll<HTMLElement>('[data-top-requester]'),
    ];

    expect(rows).toHaveLength(2);
    expect(rows[0]!.textContent).toContain('DukaSoft');
    expect(rows[0]!.textContent).toContain('266 songs');
    expect(rows[0]!.querySelector<HTMLElement>('[data-bar]')?.style.width).toBe('100%');
    expect(rows[1]!.querySelector<HTMLElement>('[data-bar]')?.style.width).toBe('50%');
  });

  it('counts the shown requests in the history footer and links the thanks footer to people', async () => {
    await open();

    expect(panel('History').querySelector('[data-history-count]')?.textContent?.trim()).toBe(
      'Showing 2 recent requests',
    );

    expect(panel('Thanks for the music').querySelector('a')?.getAttribute('href')).toBe(
      '/app/people',
    );
  });

  it('shows the empty text when nothing was requested yet', async () => {
    answers['/music/history'] = () => ok([]);
    await open();

    expect(panel('History').textContent).toContain(en.dashboard.music.history.empty);
  });
});

describe('MusicView', () => {
  it('shows the playing track and the queue rows from the queue answer', async () => {
    await open();

    const now = panel('Now playing').textContent ?? '';

    expect(now).toContain('Midnight City');
    expect(now).toContain('M83');
    expect(now).toContain('DukaSoft');
    const rows = panel('Request queue').textContent ?? '';

    expect(rows).toContain('Strobe');
    expect(rows).toContain('deadmau5');
    expect(rows).toContain('sample_viewer');
    expect(rows).toContain('Opus');
    expect(document.body.textContent).toContain('Spotify connected');
  });

  it('skips with a POST to the skip path', async () => {
    await open();

    buttonIn(panel('Now playing'), 'Next song').click();
    await settle();

    expect(writes()).toHaveLength(1);
    expect(writes()[0]!.method).toBe('POST');
    expect(writes()[0]!.path.endsWith('/music/skip')).toBe(true);
  });

  it('closes the queue with a PUT, then shows it closed and offers to open it', async () => {
    await open();

    expect(panel('Request queue').textContent).toContain('Open');

    buttonIn(panel('Request queue'), 'Close queue').click();
    await settle();

    expect(writes()).toHaveLength(1);
    expect(writes()[0]!.method).toBe('PUT');
    expect(writes()[0]!.path.endsWith('/music/queue/requests-open')).toBe(true);
    expect(writes()[0]!.body).toEqual({ open: false });
    expect(panel('Request queue').textContent).toContain('Closed');
    expect(panel('Request queue').textContent).toContain('Open queue');
  });

  it('hides the queue state button from someone without the queue moderation grant', async () => {
    heldActionKeys = ['music:queue:view'];

    await open();

    expect(panel('Request queue').textContent).toContain('Open');
    expect(panel('Request queue').querySelector('[data-toggle-requests]')).toBeNull();
  });

  it('pauses a playing track and offers Play for a paused one', async () => {
    await open();

    buttonIn(panel('Now playing'), 'Pause').click();
    await settle();

    expect(writes()[0]!.path.endsWith('/music/pause')).toBe(true);
    nowPlaying = playing({ isPlaying: false });
    mounted?.unmount();
    await open();
    calls.length = 0;
    buttonIn(panel('Now playing'), 'Play').click();
    await settle();

    expect(writes()[0]!.path.endsWith('/music/resume')).toBe(true);
  });

  it('disables the controls the player says it cannot do', async () => {
    nowPlaying = playing({ canSkipNext: false, canPause: false });
    await open();

    expect(buttonIn(panel('Now playing'), 'Next song').disabled).toBe(true);
    expect(buttonIn(panel('Now playing'), 'Pause').disabled).toBe(true);
  });

  it('removes a row only after the confirm, sends its position, and the row leaves', async () => {
    await open();

    buttonIn(panel('Request queue'), 'Remove Opus').click();
    await settle();
    expect(writes()).toHaveLength(0);

    document.body.querySelector<HTMLButtonElement>('[data-confirm-action]')!.click();
    await settle();

    expect(writes()).toHaveLength(1);
    expect(writes()[0]!.method).toBe('DELETE');
    expect(writes()[0]!.path.endsWith('/music/queue/{position}')).toBe(true);
    expect(writes()[0]!.position).toBe(2);
    expect(panel('Request queue').textContent).not.toContain('Opus');
    expect(panel('Request queue').textContent).toContain('Strobe');
  });

  // Stoney 2026-10-16: "i need my ban song button."
  it('bans a row only after the confirm: a POST to ban with its position, and the row leaves', async () => {
    await open();

    buttonIn(panel('Request queue'), 'Ban Opus').click();
    await settle();
    expect(writes()).toHaveLength(0);

    const dialog = document.body.querySelector<HTMLElement>('[role="alertdialog"]')!;

    expect(dialog.textContent).toContain('Ban this song?');
    expect(dialog.querySelector('[data-confirm-song]')?.textContent).toBe('Opus');

    document.body.querySelector<HTMLButtonElement>('[data-confirm-action]')!.click();
    await settle();

    expect(writes()).toHaveLength(1);
    expect(writes()[0]!.method).toBe('POST');
    expect(writes()[0]!.path.endsWith('/music/queue/{position}/ban')).toBe(true);
    expect(writes()[0]!.position).toBe(2);
    expect(panel('Request queue').textContent).not.toContain('Opus');
    expect(panel('Request queue').textContent).toContain('Strobe');
  });

  // Owner 2026-10-15: the confirm must name who asked and lead with the song itself.
  it('shows the song, its artist and who asked for it in the remove confirm', async () => {
    await open();

    buttonIn(panel('Request queue'), 'Remove Opus').click();
    await settle();

    const dialog = document.body.querySelector<HTMLElement>('[role="alertdialog"]')!;

    expect(dialog.querySelector('[data-confirm-song]')?.textContent).toBe('Opus');
    expect(dialog.querySelector('[data-confirm-subject]')?.textContent).toContain('Eric Prydz');
    expect(dialog.textContent).toContain('Requested by DukaSoft');
  });

  it('moves a song up with a POST to promote and its position', async () => {
    await open();

    buttonIn(panel('Request queue'), 'Play next Opus').click();
    await settle();

    expect(writes()[0]!.path.endsWith('/music/queue/{position}/promote')).toBe(true);
    expect(writes()[0]!.position).toBe(2);
  });

  // Stoney 2026-10-14: "please make this 3 rows and a bigger image".
  it('shows a queued song as title, artist and requester on three lines', async () => {
    await open();

    const row = panel('Request queue').querySelector('[data-queue-item]');
    const lines = [
      ...(row?.querySelectorAll('span.block, span.flex.items-center.gap-2') ?? []),
    ].map((line) => line.textContent?.trim());

    expect(lines).toHaveLength(3);
    expect(lines[2]).toBe('Requested by sample_viewer');
    expect(lines[1]).not.toContain('Requested');
  });

  // Bamo 2026-10-16: "are the tracks not numbered in this list?" The number is the server's, the same one
  // !queue prints and !wrongsong reads back, so it is shown as given and never counted from the row index.
  it('shows the number the server gave each queued song as #N', async () => {
    queue = [
      { ...track(1, 'Strobe', 'deadmau5', 'Bamo'), number: 4 },
      { ...track(2, 'Opus', 'Eric Prydz', 'Spotify'), number: 5 },
    ];

    await open();

    const numbers = [...panel('Request queue').querySelectorAll('[data-queue-number]')].map(
      (node) => node.textContent?.trim(),
    );

    expect(numbers).toEqual(['#4', '#5']);
  });

  // Bamo 2026-10-15: name colors in the music queue; a click opens the profile.
  it('shows a queued requester in their chat color and opens their profile on click', async () => {
    queue = [
      {
        ...track(1, 'Strobe', 'deadmau5', 'Bamo'),
        requesterId: '300',
        requesterColor: '#FF69B4',
      },
      track(2, 'Opus', 'Eric Prydz', 'Spotify'),
    ];

    await open();

    const rows = [...panel('Request queue').querySelectorAll<HTMLElement>('[data-queue-item]')];
    const named = rows[0]!.querySelector<HTMLElement>('[data-slot="user-name"]');

    expect(named?.getAttribute('data-user')).toBe('300');
    expect(named?.textContent?.trim()).toBe('Bamo');
    expect(named?.style.color).not.toBe('');
    expect(rows[0]!.textContent).toContain('Requested by Bamo');
    expect(rows[1]!.querySelector('[data-slot="user-name"]')).toBeNull();
    expect(rows[1]!.textContent).toContain('Requested by Spotify');
  });

  // Stoney 2026-10-13: "i need to be able to drag mine to the end haha".
  it('drags a song by its handle to the end: a POST to move with the drop position, and the rows follow', async () => {
    queue.push(track(3, 'Lose Yourself', 'Eminem', 'Stoney_Eagle'));
    answers = freshAnswers();
    await open();

    expect(
      panel('Request queue').querySelectorAll('[data-queue-item] [data-drag-handle]'),
    ).toHaveLength(3);

    drop(0, 2);
    await settle();

    expect(writes()).toHaveLength(1);
    expect(writes()[0]!.path.endsWith('/music/queue/{position}/move')).toBe(true);
    expect(writes()[0]!.position).toBe(1);
    expect(writes()[0]!.body).toEqual({ toPosition: 3 });

    const titles: string[] = [...panel('Request queue').querySelectorAll('[data-queue-item]')].map(
      (row) => row.querySelector('.font-medium')?.textContent ?? '',
    );

    expect(titles).toEqual(['Opus', 'Lose Yourself', 'Strobe']);
  });

  // Sortable moves the dragged node itself. The page must put it back so Vue, not the library, owns the
  // order on screen; otherwise the rows show the dragged order while the server holds another.
  it('shows the rows in the order the server now holds after a drag, not the library-moved DOM order', async () => {
    queue.push(track(3, 'Lose Yourself', 'Eminem', 'Stoney_Eagle'));
    answers = freshAnswers();
    await open();

    const list = panel('Request queue').querySelector('[data-queue-list]') as HTMLElement;

    drop(0, 2);

    await settle();

    const titles: string[] = [...list.querySelectorAll('[data-queue-item]')].map(
      (row) => row.querySelector('.font-medium')?.textContent ?? '',
    );

    expect(titles).toEqual(['Opus', 'Lose Yourself', 'Strobe']);
  });

  it('sends nothing when a song is dropped where it started', async () => {
    await open();

    drop(1, 1);
    await settle();

    expect(writes()).toHaveLength(0);
  });

  it('says nothing is playing when the server leaves the track out', async () => {
    answers['/music/queue'] = () => ok({ queue: [], requestsOpen: true });
    await open();

    expect(panel('Now playing').querySelector('[data-slot="panel-empty"]')?.textContent).toContain(
      'Nothing is playing right now.',
    );
  });

  it('says the queue could not load when the server refuses it', async () => {
    answers['/music/queue'] = refused;
    await open();

    expect(
      panel('Request queue').querySelector('[data-slot="panel-error"]')?.textContent,
    ).toContain('The queue could not load.');
  });

  it('says why history and thanks are empty instead of showing made-up rows', async () => {
    answers['/music/history'] = () => ok([]);
    answers['/music/top-requesters'] = () => ok([]);
    await open();

    expect(panel('History').querySelector('[data-slot="panel-empty"]')?.textContent).toContain(
      'Not recorded yet',
    );

    expect(
      panel('Thanks for the music').querySelector('[data-slot="panel-empty"]')?.textContent,
    ).toContain('Not recorded yet');
  });

  // Owner 2026-10-14: "make the rules a slide in / pop over". The board keeps no rules column.
  it('keeps the rules off the board and slides them in from the header button', async () => {
    await open();

    expect(panel('Request rules')).toBeUndefined();

    buttonIn(document.body, 'Request rules').click();
    await flushPromises();

    const sheet = document.body.querySelector<HTMLElement>('[data-slot="side-panel"]');

    expect(sheet?.querySelector('[data-slot="panel-empty"]')?.textContent).toContain(
      'Not available yet',
    );
  });

  // Owner 2026-10-19, "sigh...": the sheet's own X sat on top of the "Requests are open" switch and read as a glyph in its thumb.
  it('carries the rules close button in the panel header, never over the header switch', async () => {
    await open();

    buttonIn(document.body, 'Request rules').click();
    await flushPromises();

    const sheet = document.body.querySelector<HTMLElement>('[data-slot="side-panel"]');
    const closes = sheet?.querySelectorAll('[data-slot="sheet-close"], [data-drawer-close]');
    const inHeader = sheet?.querySelectorAll('[data-slot="panel-actions"] [data-drawer-close]');

    expect(closes).toHaveLength(1);
    expect(inHeader).toHaveLength(1);
    expect(sheet?.querySelector('[class*="absolute"][data-slot="sheet-close"]')).toBeNull();
  });
});
