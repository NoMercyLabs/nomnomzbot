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

import { chooseTarget, openTargetPicker } from '@/components/layout/__tests__/targetPickerDom';
import { i18n } from '@/i18n';

import { rungNames } from '@/api/permissions';
import CommandsView from '../CommandsView.vue';
import en from '../i18n/en.json';
import scriptsEn from '../../scripts/i18n/en.json';

type Options = { params?: { path?: Record<string, unknown> }; body?: Record<string, unknown> };
type Call = {
  method: string;
  path: string;
  name: unknown;
  body: Record<string, unknown> | undefined;
};
type Answer = {
  data?: unknown;
  error?: unknown;
  response: { status: number; ok: boolean };
};
type Row = Record<string, unknown>;

const calls: Call[] = [];
let answers: Record<string, () => Answer> = {};
let rows: Row[] = [];
let scripts: Row[] = [];
let refusePut: string | null = null;
let refusePost: string | null = null;
let held: string[] = ['commands:read', 'commands:write'];

const ok = (data: unknown): Answer => ({ data: { data }, response: { status: 200, ok: true } });
const refused = (message: string, status: number = 400): Answer => ({
  error: { message },
  response: { status, ok: false },
});

const command = (name: string, overrides: Row = {}) =>
  ({
    id: `id-${name}`,
    name,
    tier: 'template',
    minPermissionLevel: 'Everyone',
    isEnabled: true,
    prefixMode: 'Default',
    customPrefix: null,
    matchMode: 'StartsWith',
    matchPattern: null,
    cooldownSeconds: 5,
    userCooldownSeconds: 10,
    description: null,
    aliases: [],
    useCount: 0,
    createdAt: '2026-10-01T10:00:00Z',
    templateResponse: 'Hello there',
    templateResponses: null,
    pipelineId: null,
    presetKey: null,
    speakWithTts: false,
    ...overrides,
  }) as const satisfies Row;

function freshAnswers() {
  return {
    '/roles/effective/me': () => ok({ heldActionKeys: held }),
    '/commands': () => ok(rows.map((row) => ({ ...row }))),
    '/pipelines': () =>
      ok([
        {
          id: 'p1',
          name: 'Welcome flow',
          isEnabled: true,
        },
      ]),
    '/code-scripts': () => ok(scripts.map((script) => ({ ...script }))),
    '/templates/helpers': () => ok([]),
    '/builtins': () =>
      ok([
        {
          builtinKey: 'uptime',
          name: '!uptime',
          isEnabled: true,
          isReserved: false,
          speakWithTts: false,
          replyOverrideCount: 0,
          defaultCooldownSeconds: 30,
          defaultUserCooldownSeconds: 60,
          defaultMinPermissionLevel: 'Everyone',
          cooldownSecondsOverride: null,
          userCooldownSecondsOverride: null,
          minPermissionLevelOverride: null,
        },
        {
          builtinKey: 'shoutout',
          name: '!shoutout',
          isEnabled: true,
          isReserved: false,
          speakWithTts: false,
          replyOverrideCount: 0,
          defaultCooldownSeconds: 10,
          defaultUserCooldownSeconds: 0,
          defaultMinPermissionLevel: 'Moderator',
          cooldownSecondsOverride: 20,
          userCooldownSecondsOverride: null,
          minPermissionLevelOverride: 'Broadcaster',
        },
        {
          builtinKey: 'permit',
          name: '!permit',
          isEnabled: true,
          isReserved: false,
          isAlwaysOn: true,
          speakWithTts: false,
          replyOverrideCount: 0,
          defaultCooldownSeconds: 0,
          defaultUserCooldownSeconds: 0,
          defaultMinPermissionLevel: 'Moderator',
          cooldownSecondsOverride: null,
          userCooldownSecondsOverride: null,
          minPermissionLevelOverride: null,
        },
        {
          builtinKey: 'mydata',
          name: '!mydata',
          isEnabled: true,
          isReserved: true,
          isAlwaysOn: true,
          speakWithTts: false,
          replyOverrideCount: 0,
          defaultCooldownSeconds: 0,
          defaultUserCooldownSeconds: 0,
          defaultMinPermissionLevel: 'Everyone',
          cooldownSecondsOverride: null,
          userCooldownSecondsOverride: null,
          minPermissionLevelOverride: null,
        },
      ]),
    '/blast-radius': () => ok({ categories: [], isMinimum: false }),
  } as const satisfies Record<string, () => Answer>;
}

function read(path: string): Answer {
  const match = Object.keys(answers).find((suffix) => path.endsWith(suffix));

  return match
    ? answers[match]!()
    : ok(null);
}

function write(method: string, path: string, options?: Options): Answer {
  calls.push({
    method,
    path,
    name: options?.params?.path?.commandName ?? options?.params?.path?.builtinKey,
    body: options?.body,
  });

  if (method === 'PUT' && refusePut)
    return refused(refusePut);

  if (method === 'POST' && refusePost)
    return refused(refusePost, 409);

  if (method === 'PUT') {
    const index = rows.findIndex((row) => row.name === options?.params?.path?.commandName);

    rows[index] = { ...rows[index], ...options?.body };
  }

  if (method === 'DELETE') {
    rows = rows.filter((row) => row.name !== options?.params?.path?.commandName);
  }

  if (method === 'POST' && path.endsWith('/pipelines')) {
    const pipeline = {
      id: 'p-new',
      name: String(options?.body?.name),
      isEnabled: true,
      graph: { steps: [] },
    };

    return { data: { data: pipeline }, response: { status: 200, ok: true } };
  }

  if (method === 'POST') {
    const name = String(options?.body?.name);

    rows.push(command(name, options?.body));
  }

  return { response: { status: 200, ok: true } };
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => read(path),
    POST: async (path: string, options?: Options) => write('POST', path, options),
    PUT: async (path: string, options?: Options) => write('PUT', path, options),
    PATCH: async (path: string, options?: Options) => write('PATCH', path, options),
    DELETE: async (path: string, options?: Options) => write('DELETE', path, options),
  },
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;
let router: Router;
let pinia: Pinia;
// The window width the media queries answer for: a desktop by default, a phone in the mobile tests.
let viewport = 1280;

function stubMatchMedia(): void {
  window.matchMedia = ((query: string): MediaQueryList => {
    const min = /min-width:\s*([\d.]+)px/.exec(query);
    const max = /max-width:\s*([\d.]+)px/.exec(query);

    return {
      matches: min
        ? viewport >= Number(min[1])
        : max !== null && viewport <= Number(max[1]),
      media: query,
      onchange: null,
      addEventListener: (): void => undefined,
      removeEventListener: (): void => undefined,
      addListener: (): void => undefined,
      removeListener: (): void => undefined,
      dispatchEvent: (): boolean => false,
    } as MediaQueryList;
  }) as typeof window.matchMedia;
}

beforeEach(() => {
  viewport = 1280;
  stubMatchMedia();
  localStorage.clear();
  calls.length = 0;

  rows = [
    command('hello', {
      useCount: 12,
      aliases: ['hi'],
      templateResponse: 'Hello there',
    }),
    command('socials', {
      useCount: 40,
      templateResponse: 'Find me on the web',
      isEnabled: false,
    }),
    command('pipe', {
      tier: 'pipeline',
      pipelineId: null,
      templateResponse: null,
      useCount: 3,
    }),
  ];

  scripts = [];
  refusePut = null;
  refusePost = null;
  held = ['commands:read', 'commands:write'];
  answers = freshAnswers();
  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
  // The delete check words what still runs a command with the scripts page's shared lines.
  i18n.global.mergeLocaleMessage('en', scriptsEn);
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

async function open(url: string = '/app/commands'): Promise<void> {
  const blank = { template: '<div />' };

  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app/commands',
        name: 'dashboard-commands',
        component: blank,
      },
      {
        path: '/app/scripts',
        name: 'dashboard-scripts',
        component: blank,
      },
    ],
  });

  await router.push(url);

  mounted = mount(CommandsView, {
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

const rowEls = (): HTMLElement[] => [
  ...document.body.querySelectorAll<HTMLElement>('[data-command-row]'),
];
const rowNames = (): (string | undefined)[] => rowEls().map((row) => row.dataset.commandName);
const editor = (): HTMLElement | null =>
  document.body.querySelector<HTMLElement>('[data-command-editor]');
const buttonByText = (label: string, root: ParentNode = document.body): HTMLButtonElement =>
  [...root.querySelectorAll<HTMLButtonElement>('button')].find(
    (b) => b.textContent?.trim() === label || b.getAttribute('aria-label') === label,
  ) as HTMLButtonElement;
const field = (label: string): HTMLInputElement =>
  document.body.querySelector<HTMLInputElement>(`[aria-label="${label}"]`) as HTMLInputElement;
const reply = (): HTMLTextAreaElement => editor()!.querySelector('textarea') as HTMLTextAreaElement;
const unsaved = (): string => editor()!.querySelector('[data-unsaved]')?.textContent?.trim() ?? '';
const switchOf = (title: string): HTMLButtonElement =>
  [...document.body.querySelectorAll<HTMLElement>('[data-slot="setting-row"], label')]
    .find((row) => row.textContent?.includes(title))
    ?.querySelector('button[role="switch"]') as HTMLButtonElement;
const writes = (method: string): Call[] => calls.filter((call) => call.method === method);

async function type(el: HTMLInputElement | HTMLTextAreaElement, value: string): Promise<void> {
  el.value = value;
  el.dispatchEvent(new Event('input', { bubbles: true }));
  await settle();
}

async function pick(name: string): Promise<void> {
  rowEls()
    .find((row) => row.dataset.commandName === name)!
    .click();

  await settle();
}

describe('CommandsView list', () => {
  it('lists every command with its type, its uses and an Off mark on a disabled one, never the reply text', async () => {
    rows[2] = command('dice', {
      templateResponse: null,
      templateResponses: ['One', 'Two'],
      useCount: 3,
    });

    await open('/app/commands?view=custom');

    expect(rowNames().sort()).toEqual(['dice', 'hello', 'socials']);
    const hello = rowEls().find((row) => row.dataset.commandName === 'hello')!.textContent ?? '';

    expect(hello).toContain('!hello');
    expect(hello).toContain('Text reply · 12 uses');
    expect(hello).not.toContain('Hello there');
    const dice = rowEls().find((row) => row.dataset.commandName === 'dice')!.textContent ?? '';

    expect(dice).toContain('Random reply · 3 uses');
    expect(dice).not.toContain('One');
    const socials =
      rowEls().find((row) => row.dataset.commandName === 'socials')!.textContent ?? '';

    expect(socials).toContain('Off');
    expect(hello).not.toContain('Off');
  });

  it('filters by name and by alias, and says so when nothing matches', async () => {
    await open();
    const search = document.body.querySelector<HTMLInputElement>(
      '[data-slot="search-field"] input',
    )!;

    await type(search, 'soc');
    expect(rowNames()).toEqual(['socials']);
    await type(search, 'hi');
    expect(rowNames()).toEqual(['hello']);
    await type(search, 'zzz');
    expect(rowNames()).toEqual([]);
    expect(document.body.textContent).toContain('No command matches');
  });

  it('orders by most used first and by name when A to Z is chosen', async () => {
    await open('/app/commands?view=custom');
    expect(rowNames()).toEqual(['socials', 'hello', 'pipe']);

    buttonByText('A to Z').click();
    await settle();

    expect(rowNames()).toEqual(['hello', 'pipe', 'socials']);
  });

  // Owner 2026-10-17: the sort leads the bar over the list column, the search sits in the list panel's header,
  // so the list and detail panels start on one line.
  it('puts the sort first in the control bar and the search in the list panel header', async () => {
    await open();
    const bar = document.body.querySelector('[data-slot="page-bar"]')!;

    expect(bar.querySelector('[data-segmented]')?.textContent).toContain('A to Z');

    expect(
      bar
        .querySelector('[data-segmented]')!
        .compareDocumentPosition(bar.querySelector('[role="tablist"]')!),
    ).toBe(Node.DOCUMENT_POSITION_FOLLOWING);

    expect(bar.querySelector('[data-slot="search-field"]')).toBeNull();

    expect(
      document.body.querySelector(
        '[data-slot="list-detail-list"] [data-slot="panel-card"] header [data-slot="search-field"]',
      ),
    ).not.toBeNull();

    mounted!.unmount();
    mounted = null;
    document.body.innerHTML = '';

    // The built-in list keeps the bot's own order, so a sort that does nothing there is not shown.
    await open('/app/commands?view=builtin');

    expect(document.body.querySelector('[data-slot="page-bar"] [data-segmented]')).toBeNull();
  });

  it('shows only the channel\'s own commands by default, with the system commands on their own tab', async () => {
    await open();

    expect(
      document.body.querySelector('[role="tab"][data-state="active"]')?.textContent?.trim(),
    ).toBe('Channel commands');

    expect(rowNames().sort()).toEqual(['hello', 'pipe', 'socials']);
    expect(buttonByText('System commands')).toBeDefined();
    expect(buttonByText('All')).toBeUndefined();
  });

  it('marks a channel command that repeats a system command, by name or by alias, and counts them', async () => {
    rows = [command('hello'), command('permit'), command('so', { aliases: ['shoutout'] })];
    await open();

    const marked = rowEls()
      .filter((row) => row.querySelector('[data-system-duplicate]') !== null)
      .map((row) => row.dataset.commandName);

    expect(marked).toEqual(['permit', 'so']);

    expect(
      rowEls()
        .find((row) => row.dataset.commandName === 'so')!
        .querySelector('[data-system-duplicate]')?.textContent?.trim(),
    ).toBe('Repeats !shoutout');

    expect(document.body.querySelector('[data-system-duplicates]')?.textContent?.trim()).toBe(
      '2 of 3 channel commands repeat a system command. The system command answers instead.',
    );
  });

  it('stacks the duplicate notice above its button, so a narrow panel never squeezes the words', async () => {
    rows = [command('hello'), command('permit'), command('so', { aliases: ['shoutout'] })];
    await open();

    const banner = document.body.querySelector('[data-system-duplicates]')?.parentElement;

    expect(banner?.classList.contains('flex-col')).toBe(true);
    expect(banner?.querySelector('[data-system-duplicates]')?.classList.contains('flex-1')).toBe(false);
  });

  it('moves every channel copy of a system command out, only after the confirm', async () => {
    rows = [command('hello'), command('permit'), command('so', { aliases: ['shoutout'] })];
    await open();

    buttonByText('Move copies to system')!.click();
    await settle();
    expect(writes('DELETE')).toHaveLength(0);

    document.body.querySelector<HTMLButtonElement>('[data-confirm-action]')!.click();
    await settle();

    expect(writes('DELETE').map((write) => write.name).sort()).toEqual(['permit', 'so']);
    expect(rowNames()).toEqual(['hello']);
    expect(document.body.querySelector('[data-system-duplicates]')).toBeNull();
  });

  it('keeps the A to Z order after the page is opened again', async () => {
    await open('/app/commands?view=custom');
    buttonByText('A to Z').click();
    await settle();
    mounted!.unmount();
    mounted = null;
    document.body.innerHTML = '';

    await open('/app/commands?view=custom');

    expect(rowNames()).toEqual(['hello', 'pipe', 'socials']);
  });

  it('shows the empty state with a way to make the first command', async () => {
    rows = [];
    await open();

    expect(document.body.textContent).toContain('No commands yet');
    expect(document.body.querySelectorAll('[data-slot="panel-empty"] button').length).toBe(1);
  });

  it('shows an error with Retry, and Retry loads the list again', async () => {
    answers['/commands'] = (): Answer => refused('boom', 500);
    await open('/app/commands?view=custom');
    expect(document.body.querySelector('[data-slot="panel-error"]')).not.toBeNull();

    answers['/commands'] = freshAnswers()['/commands']!;
    buttonByText('Try again').click();
    await settle();

    expect(rowNames().length).toBe(3);
  });
});

describe('CommandsView editor', () => {
  it('opens the command named in the address and fills the form from its row', async () => {
    rows[0] = command('hello', {
      templateResponse: 'Hi chat',
      cooldownSeconds: 7,
      userCooldownSeconds: 30,
      speakWithTts: true,
    });

    await open('/app/commands?command=hello');

    expect(editor()!.textContent).toContain('!hello');
    expect(reply().value).toBe('Hi chat');
    expect(field('Chat cooldown (seconds)').value).toBe('7');
    expect(field('Per-person cooldown (seconds)').value).toBe('30');
    expect(switchOf('Read it aloud').getAttribute('aria-checked')).toBe('true');
  });

  it('keeps the picked command in the address so a refresh keeps it', async () => {
    await open();
    expect(editor()).toBeNull();

    const detail = (): string =>
      document.body.querySelector('[data-slot="list-detail-detail"]')!.className;

    // Nothing open: the hint keeps its own height. A command open: the editor fills the column again.
    expect(detail()).toContain('md:*:last:flex-1');

    await pick('socials');

    expect(detail()).toContain('md:*:last:flex-1');

    expect(router.currentRoute.value.query.command).toBe('socials');
    expect(reply().value).toBe('Find me on the web');
  });

  it('sends only the changed fields on Save and clears the unsaved count', async () => {
    await open('/app/commands?command=hello');
    expect(unsaved()).toBe('');

    await type(reply(), 'Hello everyone');
    await type(field('Chat cooldown (seconds)'), '20');
    expect(unsaved()).toBe('2 unsaved changes');

    buttonByText('Save changes').click();
    await settle();

    expect(writes('PUT')).toHaveLength(1);
    expect(writes('PUT')[0]!.name).toBe('hello');

    expect(writes('PUT')[0]!.body).toEqual({
      templateResponse: 'Hello everyone',
      cooldownSeconds: 20,
    });

    expect(unsaved()).toBe('');
    expect(buttonByText('Save changes').disabled).toBe(true);
  });

  it('Discard puts the saved values back and sends nothing', async () => {
    await open('/app/commands?command=hello');
    await type(reply(), 'Something else');
    expect(unsaved()).toBe('1 unsaved change');

    buttonByText('Discard').click();
    await settle();

    expect(reply().value).toBe('Hello there');
    expect(unsaved()).toBe('');
    expect(calls).toHaveLength(0);
  });

  it('keeps the form dirty and shows the bot message when a save is refused', async () => {
    refusePut = 'Unknown helper {zzz}';
    await open('/app/commands?command=hello');
    await type(reply(), 'Hi {zzz}');

    buttonByText('Save changes').click();
    await settle();

    expect(editor()!.querySelector('[role="alert"]')?.textContent).toContain(
      'Unknown helper {zzz}',
    );

    expect(unsaved()).toBe('1 unsaved change');
    expect(reply().value).toBe('Hi {zzz}');
  });

  it('switches a command off with a PUT of isEnabled only, and puts it back when refused', async () => {
    await open('/app/commands?command=hello');

    switchOf('Enabled').click();
    await settle();
    expect(writes('PUT')[0]!.body).toEqual({ isEnabled: false });

    expect(rowEls().find((row) => row.dataset.commandName === 'hello')!.textContent).toContain(
      'Off',
    );

    refusePut = 'No right';
    switchOf('Enabled').click();
    await settle();

    expect(rowEls().find((row) => row.dataset.commandName === 'hello')!.textContent).toContain(
      'Off',
    );
  });

  it('sends speakWithTts when Read it aloud is switched on', async () => {
    await open('/app/commands?command=hello');

    switchOf('Read it aloud').click();
    await settle();
    expect(unsaved()).toBe('1 unsaved change');
    buttonByText('Save changes').click();
    await settle();

    expect(writes('PUT')[0]!.body).toEqual({ speakWithTts: true });
  });

  it('switches a text command to Random reply in place: a reply count and an Edit replies button, the text seeds the first reply', async () => {
    await open('/app/commands?command=hello');
    const panel = (): HTMLElement | null =>
      document.body.querySelector<HTMLElement>('[data-random-panel]');

    buttonByText('Random reply').click();
    await settle();

    expect(panel()).toBeNull();
    expect(editor()!.querySelector('[data-random-summary]')?.textContent).toContain('1 reply');
    expect(editor()!.querySelector('[data-reply-pipeline]')).toBeNull();
    buttonByText('Edit replies').click();
    await settle();
    const first = panel()!.querySelector<HTMLTextAreaElement>('[data-random-reply] textarea')!;

    expect(first.value).toBe('Hello there');
  });

  it('edits the random replies in the dialog and sends them as templateResponses on Save', async () => {
    await open('/app/commands?command=hello');

    buttonByText('Random reply').click();
    await settle();
    buttonByText('Edit replies').click();
    await settle();
    const inputs = (): HTMLTextAreaElement[] => [
      ...document.body.querySelectorAll<HTMLTextAreaElement>(
        '[data-random-panel] [data-random-reply] textarea',
      ),
    ];

    await type(inputs()[0]!, 'One');
    buttonByText('Add a reply').click();
    await settle();
    await type(inputs()[1]!, 'Two');
    buttonByText('Use these replies').click();
    await settle();

    expect(editor()!.querySelector('[data-random-summary]')?.textContent).toContain('2 replies');
    buttonByText('Save changes').click();
    await settle();

    expect(writes('PUT')[0]!.body).toEqual({ templateResponses: ['One', 'Two'] });
  });

  it('switching back to Text keeps the first reply as the text and clears the list', async () => {
    rows[2] = command('dice', { templateResponse: null, templateResponses: ['One', 'Two'] });
    await open('/app/commands?command=dice');

    buttonByText('Text').click();
    await settle();

    expect(document.body.querySelector('[data-random-panel]')).toBeNull();
    expect(reply().value).toBe('One');
    buttonByText('Save changes').click();
    await settle();

    expect(writes('PUT')[0]!.body).toEqual({ templateResponse: 'One', templateResponses: [] });
  });

  it('chooses an existing reply set from another command and sends a copy of it', async () => {
    rows[2] = command('dice', { templateResponse: null, templateResponses: ['Red', 'Blue'] });
    await open('/app/commands?command=hello');

    buttonByText('Random reply').click();
    await settle();
    buttonByText('Edit replies').click();
    await settle();
    buttonByText('Use the replies of !dice').click();
    await settle();
    const inputs = [
      ...document.body.querySelectorAll<HTMLTextAreaElement>(
        '[data-random-panel] [data-random-reply] textarea',
      ),
    ];

    expect(inputs.map((input) => input.value)).toEqual(['Red', 'Blue']);
    buttonByText('Use these replies').click();
    await settle();
    buttonByText('Save changes').click();
    await settle();

    expect(writes('PUT')[0]!.body).toEqual({ templateResponses: ['Red', 'Blue'] });
  });

  it('keeps every reply when the owner looks at Text and comes back, with nothing unsaved', async () => {
    rows[2] = command('dice', {
      templateResponse: null,
      templateResponses: ['One', 'Two', 'Three'],
    });

    await open('/app/commands?command=dice');

    buttonByText('Text').click();
    await settle();
    buttonByText('Random reply').click();
    await settle();

    expect(editor()!.querySelector('[data-random-summary]')?.textContent).toContain('3 replies');
    expect(unsaved()).toBe('');
  });

  it('drops dialog edits on Cancel, so the draft keeps its replies', async () => {
    rows[2] = command('dice', { templateResponse: null, templateResponses: ['One'] });
    await open('/app/commands?command=dice');

    buttonByText('Edit replies').click();
    await settle();
    buttonByText('Add a reply').click();
    await settle();
    buttonByText('Cancel').click();
    await settle();

    expect(editor()!.querySelector('[data-random-summary]')?.textContent).toContain('1 reply');
    expect(unsaved()).toBe('');
  });

  it('removes a command only after the confirm, with a DELETE for that name', async () => {
    await open('/app/commands?command=hello');

    buttonByText('Delete').click();
    await settle();
    expect(writes('DELETE')).toHaveLength(0);

    document.body.querySelector<HTMLButtonElement>('[data-confirm-action]')!.click();
    await settle();

    expect(writes('DELETE')).toHaveLength(1);
    expect(writes('DELETE')[0]!.name).toBe('hello');
    expect(rowNames()).not.toContain('hello');
    expect(editor()).toBeNull();
    expect(router.currentRoute.value.query.command).toBeUndefined();
  });

  it('duplicates a command as a switched-off copy without its aliases, and opens the copy', async () => {
    await open('/app/commands?command=hello');

    buttonByText('Duplicate').click();
    await settle();

    const posts = writes('POST');

    expect(posts).toHaveLength(1);
    expect(posts[0]!.body).toMatchObject({ name: 'hello-copy', isEnabled: false });
    expect(posts[0]!.body).not.toHaveProperty('aliases');
    expect(rowNames()).toContain('hello-copy');
    expect(router.currentRoute.value.query.command).toBe('hello-copy');
  });

  it('names what still runs a command and opens no confirm, so a delete the bot would refuse is never sent', async () => {
    answers['/blast-radius'] = () =>
      ok({
        categories: [
          {
            categoryKey: 'blast_radius_category_rewards',
            count: 2,
            sample: ['Hydrate', 'Stretch'],
          },
          {
            categoryKey: 'blast_radius_category_timers',
            count: 1,
            sample: ['Follow reminder'],
          },
        ],
        isMinimum: false,
      });

    await open('/app/commands?command=hello');

    buttonByText('Delete').click();
    await settle();

    expect(document.body.textContent).toContain('2 rewards: Hydrate, Stretch');
    expect(document.body.textContent).toContain('1 timers: Follow reminder');
    expect(document.body.textContent).toContain('Move or remove these first');
    expect(document.body.querySelector('[data-confirm-action]')).toBeNull();
    expect(writes('DELETE')).toHaveLength(0);
  });

  it('shows Reset to preset only on a command seeded from a preset', async () => {
    rows[0] = command('hug', { presetKey: 'hug' });
    await open('/app/commands?command=hello');
    expect(buttonByText('Reset to preset')).toBeUndefined();

    await router.push('/app/commands?command=hug');
    await settle();

    expect(buttonByText('Reset to preset').dataset.variant).toBe('outline');
  });

  it('wraps the editor footer so four buttons never overflow a narrow screen', async () => {
    rows[0] = command('hug', { presetKey: 'hug' });
    await open('/app/commands?command=hug');

    const footer = buttonByText('Reset to preset').parentElement;

    expect(footer?.classList.contains('flex-wrap')).toBe(true);
  });

  it('makes the footer buttons and New command 36px on a phone', async () => {
    rows[0] = command('hug', { presetKey: 'hug' });
    await open('/app/commands?command=hug');

    const labels = ['Delete', 'Reset to preset', 'Discard', 'Save changes', 'New command'];

    const missingPhoneHeight = labels.filter((label) =>
      !buttonByText(label).classList.contains('h-9'));
    const missingDesktopHeight = labels.filter((label) =>
      !buttonByText(label).classList.contains('md:h-8'));

    expect(missingPhoneHeight).toEqual([]);
    expect(missingDesktopHeight).toEqual([]);
  });

  it('hides the sort and tabs bar on a phone while a command is open, and keeps it on the list', async () => {
    await open('/app/commands?command=hello');

    const bar = document.body.querySelector('[data-slot="page-bar"]')!;

    expect(bar.classList.contains('max-md:hidden')).toBe(true);

    await router.push('/app/commands');
    await settle();

    expect(
      document.body.querySelector('[data-slot="page-bar"]')!.classList.contains('max-md:hidden'),
    ).toBe(false);
  });

  it('lets the empty detail pane fill the column like the list', async () => {
    await open('/app/commands');

    const detail = document.body.querySelector('[data-slot="list-detail-detail"]')!;

    expect(detail.className).not.toContain('md:*:last:flex-none');
  });

  it('resets to the preset only after the confirm, naming what changes, then reads the list again', async () => {
    rows[0] = command('hug', {
      presetKey: 'hug',
      templateResponse: 'custom text',
      cooldownSeconds: 30,
    });

    answers['/command-presets'] = () =>
      ok([
        {
          key: 'hug',
          tier: 'template',
          description: null,
          templateResponse: 'hugs',
          templateResponses: [],
          minPermissionLevel: 'Everyone',
          cooldownSeconds: 5,
          userCooldownSeconds: 10,
          aliases: [],
          prefixMode: 'Default',
          matchMode: 'StartsWith',
        },
      ]);

    await open('/app/commands?command=hug');

    buttonByText('Reset to preset').click();
    await settle();
    expect(writes('POST')).toHaveLength(0);

    const dialog = document.body
      .querySelector<HTMLElement>('[data-confirm-action]')!
      .closest('[role="alertdialog"], [role="dialog"]')!;

    expect(dialog.textContent).toContain('Reply');
    expect(dialog.textContent).toContain('Chat cooldown');
    expect(dialog.textContent).not.toContain('Aliases');

    document.body.querySelector<HTMLButtonElement>('[data-confirm-action]')!.click();
    await settle();

    expect(writes('POST')).toHaveLength(1);

    expect(writes('POST')[0]!.path).toBe(
      '/api/v{version}/channels/{channelId}/commands/{commandName}/reset-to-preset',
    );

    expect(writes('POST')[0]!.name).toBe('hug');
  });

  it('gives focus back to the Reset to preset button when Escape closes its confirm, and writes nothing', async () => {
    rows[0] = command('hug', { presetKey: 'hug' });
    await open('/app/commands?command=hug');

    const reset = buttonByText('Reset to preset');

    reset.focus();
    reset.click();
    await settle();

    const dialog = document.body.querySelector<HTMLElement>('[role="alertdialog"]')!;

    dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();

    expect(document.body.querySelector('[role="alertdialog"]')).toBeNull();
    expect(document.activeElement).toBe(reset);
    expect(document.activeElement?.hasAttribute('data-command-row')).toBe(false);
    expect(writes('POST')).toHaveLength(0);
  });

  it('offers a way back to the list that closes the open command', async () => {
    await open('/app/commands?command=hello');
    const back = document.body.querySelector<HTMLButtonElement>('[data-detail-back]');

    expect(back?.textContent).toContain('Back to the list');
    back!.click();
    await settle();

    expect(router.currentRoute.value.query.command).toBeUndefined();
    expect(editor()).toBeNull();
  });

  it('keeps one strong button: Save leads, and Delete is quiet, never the filled danger look', async () => {
    await open('/app/commands?command=hello');

    expect(buttonByText('Save changes').dataset.variant).toBeUndefined();
    expect(buttonByText('Delete').dataset.variant).toBe('outline');
  });

  it('tells the streamer a pipeline command with no pipeline answers nothing', async () => {
    await open('/app/commands?command=pipe');

    expect(editor()!.textContent).toContain('answers nothing');
    expect(editor()!.querySelector('textarea')).toBeNull();
  });

  it('shows the reply or the pipeline picker, never both, and brings the reply back on Simple', async () => {
    await open('/app/commands?command=hello');
    expect(reply()).not.toBeNull();
    expect(editor()!.querySelector('[data-reply-pipeline]')).toBeNull();

    buttonByText('Pipeline').click();
    await settle();

    expect(editor()!.querySelector('textarea')).toBeNull();
    expect(editor()!.querySelector('[data-reply-pipeline]')).not.toBeNull();

    buttonByText('Text').click();
    await settle();

    expect(reply().value).toBe('Hello there');
    expect(editor()!.querySelector('[data-reply-pipeline]')).toBeNull();
  });

  it('holds text, random reply, code script and pipeline in one reply control', async () => {
    await open('/app/commands?command=hello');

    const controls = editor()!.querySelectorAll('[data-reply-mode] [data-segmented]');
    const labels = [...controls[0]!.querySelectorAll('button')].map((button) =>
      button.textContent?.trim(),
    );

    expect(controls).toHaveLength(1);
    expect(labels).toEqual(['Text', 'Random reply', 'Code script', 'Pipeline']);
  });

  it('creates a new pipeline from the picker and saves the command with it', async () => {
    await open('/app/commands?command=hello');
    buttonByText('Pipeline').click();
    await settle();
    Element.prototype.hasPointerCapture = (): boolean => false;
    Element.prototype.releasePointerCapture = (): void => undefined;
    Element.prototype.scrollIntoView = (): void => undefined;

    await openTargetPicker(editor()!);
    await chooseTarget('Create a new pipeline');
    await settle();

    expect(writes('POST')).toHaveLength(1);
    expect(writes('POST')[0]!.body).toMatchObject({ name: '!hello', graph: { steps: [] } });

    buttonByText('Save changes').click();
    await settle();

    expect(writes('PUT')[0]!.body).toEqual({ tier: 'pipeline', pipelineId: 'p-new' });
  });

  it('saves a command switched to Full pipeline as the pipeline tier with its pipeline, and no reply text', async () => {
    rows[0] = command('hello', { pipelineId: 'p1' });

    await open('/app/commands?command=hello');
    buttonByText('Pipeline').click();
    await settle();
    buttonByText('Save changes').click();
    await settle();

    expect(writes('PUT')).toHaveLength(1);
    expect(writes('PUT')[0]!.body).toEqual({ tier: 'pipeline', pipelineId: 'p1' });
  });

  it('saves a pipeline command switched to Simple reply as the template tier with its reply', async () => {
    rows[2] = command('pipe', {
      tier: 'pipeline',
      pipelineId: 'p1',
      templateResponse: 'Back to text',
    });

    await open('/app/commands?command=pipe');
    expect(editor()!.querySelector('textarea')).toBeNull();

    buttonByText('Text').click();
    await settle();
    expect(reply().value).toBe('Back to text');

    buttonByText('Save changes').click();
    await settle();

    expect(writes('PUT')[0]!.body).toEqual({ tier: 'template' });
  });

  it('has no Followers rung in the role list', () => {
    expect(rungNames).toEqual([
      'Everyone',
      'Subscriber',
      'Vip',
      'Artist',
      'Moderator',
      'LeadModerator',
      'Editor',
      'Broadcaster',
    ]);
  });
});

describe('CommandsView new command', () => {
  async function openDialog(): Promise<void> {
    buttonByText('New command').click();
    await settle();
  }

  it('creates a template command with a POST of the cleaned name and the reply, then opens it', async () => {
    await open();
    await openDialog();

    await type(document.body.querySelector<HTMLInputElement>('[data-new-name]')!, '  !Welcome ');

    await type(
      document.body.querySelector<HTMLTextAreaElement>('[role="dialog"] textarea')!,
      'Welcome in',
    );

    document.body.querySelector<HTMLButtonElement>('[data-form-action]')!.click();
    await settle();

    expect(writes('POST')).toHaveLength(1);

    expect(writes('POST')[0]!.body).toMatchObject({
      name: 'welcome',
      tier: 'template',
      templateResponse: 'Welcome in',
    });

    expect(rowNames()).toContain('welcome');
    expect(router.currentRoute.value.query.command).toBe('welcome');
    expect(document.body.querySelector('[role="dialog"]')).toBeNull();
  });

  it('keeps the dialog open and shows the bot message when the name is taken', async () => {
    refusePost = 'A command named hello already exists';
    await open();
    await openDialog();

    await type(document.body.querySelector<HTMLInputElement>('[data-new-name]')!, 'hello');
    await type(document.body.querySelector<HTMLTextAreaElement>('[role="dialog"] textarea')!, 'x');
    document.body.querySelector<HTMLButtonElement>('[data-form-action]')!.click();
    await settle();

    expect(document.body.querySelector('[data-form-error]')?.textContent).toContain(
      'already exists',
    );

    expect(document.body.querySelector('[role="dialog"]')).not.toBeNull();
  });

  it('does not send a command without a name', async () => {
    await open();
    await openDialog();

    document.body.querySelector<HTMLButtonElement>('[data-form-action]')!.click();
    await settle();

    expect(writes('POST')).toHaveLength(0);
  });
});

describe('CommandsView trigger options', () => {
  const inEditor = (label: string): HTMLButtonElement => buttonByText(label, editor()!);
  const prefixInput = (): HTMLInputElement | null =>
    editor()!.querySelector<HTMLInputElement>('[aria-label="Custom prefix"]');
  const patternInput = (): HTMLInputElement | null =>
    editor()!.querySelector<HTMLInputElement>('[aria-label="Regex pattern"]');

  it('shows the custom prefix field only while the prefix is Custom, and the pattern only for Regex', async () => {
    await open('/app/commands?command=hello');

    expect(prefixInput()).toBeNull();
    expect(patternInput()).toBeNull();

    inEditor('Custom').click();
    await settle();
    expect(prefixInput()).not.toBeNull();
    expect(patternInput()).toBeNull();

    inEditor('None').click();
    await settle();
    expect(prefixInput()).toBeNull();

    inEditor('Regex').click();
    await settle();
    expect(patternInput()).not.toBeNull();

    inEditor('Exact').click();
    await settle();
    expect(patternInput()).toBeNull();
  });

  it('fills the options from the saved command', async () => {
    rows[0] = command('hello', {
      prefixMode: 'Custom',
      customPrefix: '$',
      matchMode: 'Regex',
      matchPattern: '^hi',
    });

    await open('/app/commands?command=hello');

    expect(prefixInput()!.value).toBe('$');
    expect(patternInput()!.value).toBe('^hi');
    expect(unsaved()).toBe('');
  });

  it('sends the chosen prefix mode and custom prefix on Save, and nothing else', async () => {
    await open('/app/commands?command=hello');
    inEditor('Custom').click();
    await settle();
    await type(prefixInput()!, '$');

    buttonByText('Save changes').click();
    await settle();

    expect(writes('PUT')[0]!.body).toEqual({ prefixMode: 'Custom', customPrefix: '$' });
  });

  it('sends the match mode and the regex pattern on Save', async () => {
    await open('/app/commands?command=hello');
    inEditor('Regex').click();
    await settle();
    await type(patternInput()!, '^hello');

    buttonByText('Save changes').click();
    await settle();

    expect(writes('PUT')[0]!.body).toEqual({ matchMode: 'Regex', matchPattern: '^hello' });
  });

  it('sends only the mode when a mode that needs no extra text is chosen', async () => {
    await open('/app/commands?command=hello');
    inEditor('None').click();
    await settle();
    inEditor('Exact').click();
    await settle();

    buttonByText('Save changes').click();
    await settle();

    expect(writes('PUT')[0]!.body).toEqual({ prefixMode: 'None', matchMode: 'Exact' });
  });

  it('keeps Save shut while Custom has no prefix or Regex has no pattern', async () => {
    await open('/app/commands?command=hello');
    inEditor('Custom').click();
    await settle();
    expect(buttonByText('Save changes').disabled).toBe(true);

    await type(prefixInput()!, '$');
    expect(buttonByText('Save changes').disabled).toBe(false);

    inEditor('Regex').click();
    await settle();
    expect(buttonByText('Save changes').disabled).toBe(true);
  });

  it('hides Read it aloud on Full pipeline and saves speakWithTts false', async () => {
    rows[0] = command('hello', { pipelineId: 'p1', speakWithTts: true });

    await open('/app/commands?command=hello');
    expect(switchOf('Read it aloud')).toBeDefined();

    inEditor('Pipeline').click();
    await settle();
    expect(switchOf('Read it aloud')).toBeUndefined();

    buttonByText('Save changes').click();
    await settle();

    expect(writes('PUT')[0]!.body).toEqual({
      tier: 'pipeline',
      pipelineId: 'p1',
      speakWithTts: false,
    });
  });

  it('creates a random-reply command with its list of replies', async () => {
    await open();
    buttonByText('New command').click();
    await settle();
    const dialog = document.body.querySelector<HTMLElement>('[role="dialog"]')!;

    await type(document.body.querySelector<HTMLInputElement>('[data-new-name]')!, 'dice');
    // jsdom lacks these three; the select calls them when it opens.
    Element.prototype.hasPointerCapture = (): boolean => false;
    Element.prototype.releasePointerCapture = (): void => undefined;
    Element.prototype.scrollIntoView = (): void => undefined;

    dialog.querySelector<HTMLElement>('[role="combobox"]')!.dispatchEvent(
      new PointerEvent('pointerdown', {
        bubbles: true,
        button: 0,
        pointerType: 'mouse',
      }),
    );

    await settle();
    const option = [...document.body.querySelectorAll<HTMLElement>('[role="option"]')].find((el) =>
      el.textContent?.includes('random reply'),
    )!;

    option.dispatchEvent(
      new PointerEvent('pointerup', {
        bubbles: true,
        button: 0,
        pointerType: 'mouse',
      }),
    );

    option.click();
    await settle();
    await type(dialog.querySelector<HTMLTextAreaElement>('textarea')!, 'One');
    document.body.querySelector<HTMLButtonElement>('[data-form-action]')!.click();
    await settle();

    expect(writes('POST')[0]!.body).toMatchObject({
      name: 'dice',
      tier: 'template',
      templateResponses: ['One'],
    });
  });

  it('creates a command with the chosen prefix and match options', async () => {
    await open();
    buttonByText('New command').click();
    await settle();
    const dialog = document.body.querySelector<HTMLElement>('[role="dialog"]')!;

    await type(document.body.querySelector<HTMLInputElement>('[data-new-name]')!, 'raid');
    await type(dialog.querySelector<HTMLTextAreaElement>('textarea')!, 'Raid time');
    buttonByText('Custom', dialog).click();
    await settle();
    await type(dialog.querySelector<HTMLInputElement>('[aria-label="Custom prefix"]')!, '$');
    buttonByText('Regex', dialog).click();
    await settle();
    await type(dialog.querySelector<HTMLInputElement>('[aria-label="Regex pattern"]')!, 'raid');
    document.body.querySelector<HTMLButtonElement>('[data-form-action]')!.click();
    await settle();

    expect(writes('POST')[0]!.body).toMatchObject({
      name: 'raid',
      prefixMode: 'Custom',
      customPrefix: '$',
      matchMode: 'Regex',
      matchPattern: 'raid',
    });
  });
});

const builtinBody = (title: string): HTMLElement =>
  [...document.body.querySelectorAll<HTMLElement>('[data-builtin-row]')].find(
    (row) => row.querySelector('[data-slot="summary-row-title"]')?.textContent === title,
  )!;
const builtinSwitch = (title: string): HTMLButtonElement =>
  builtinBody(title).parentElement!.querySelector('button[role="switch"]') as HTMLButtonElement;

describe('CommandsView built-in tab', () => {
  it('lists the built-in commands and switches one off with a PATCH of enabled', async () => {
    await open('/app/commands?view=builtin');

    expect(document.body.textContent).toContain('!uptime');
    builtinSwitch('!uptime').click();
    await settle();

    expect(writes('PATCH')).toHaveLength(1);
    expect(writes('PATCH')[0]!.name).toBe('uptime');
    expect(writes('PATCH')[0]!.body).toEqual({ enabled: false });
  });

  it('opens the editor from a row without switching the command, and keeps it in the address', async () => {
    await open('/app/commands?view=builtin');
    builtinBody('!uptime').click();
    await settle();

    expect(writes('PATCH')).toHaveLength(0);
    expect(router.currentRoute.value.query.builtin).toBe('uptime');
    expect(router.currentRoute.value.query.view).toBe('builtin');
    const detail = document.body.querySelector('[data-builtin-detail]')!;

    expect(detail.textContent).toContain('!uptime');
    expect(detail.textContent).toContain('Customize');

    expect(
      builtinBody('!uptime')
        .parentElement!.querySelector('button[role="switch"]')
        ?.getAttribute('aria-checked'),
    ).toBe('true');
  });
});

describe('CommandsView built-in rows', () => {
  it('shows who may use each built-in, its cooldowns and whether it differs from the default', async () => {
    await open('/app/commands?view=builtin');

    const plain = builtinBody('!uptime');

    expect(
      plain.querySelector('[data-slot="summary-row-text"] > span:nth-child(2)')?.textContent,
    ).toBe('Everyone · 30s chat · 60s each');

    expect(plain.querySelector('[data-slot="summary-row-meta"]')?.textContent).toBe(
      'Using the default',
    );

    const changed = builtinBody('!shoutout');

    expect(
      changed.querySelector('[data-slot="summary-row-text"] > span:nth-child(2)')?.textContent,
    ).toBe('Broadcaster · 20s chat · 0s each');

    expect(changed.querySelector('[data-slot="summary-row-meta"]')?.textContent).toBe(
      'Overrides the default (Moderator · 10s chat · 0s each)',
    );
  });

  it('locks a data-rights built-in on and says why', async () => {
    await open('/app/commands?view=builtin');

    const reserved = builtinBody('!mydata');

    expect(reserved.parentElement!.querySelector('button[role="switch"]')).toBeNull();

    expect(
      reserved.querySelector('[data-slot="summary-row-text"] > span:nth-child(2)')?.textContent,
    ).toBe(en.dashboard.commands.builtin.reserved);
  });
});

describe('CommandsView permission gating', () => {
  it('shows a reader the list only: no New command, no editor, no system tab', async () => {
    held = ['commands:read'];
    await open('/app/commands?command=hello');

    expect(rowNames()).toContain('hello');
    expect(buttonByText('New command')).toBeUndefined();
    expect(editor()).toBeNull();
    expect(buttonByText('System commands')).toBeUndefined();
  });

  it('shows a person with commands:write the New command button, the editor and the system tab', async () => {
    await open('/app/commands?command=hello');

    expect(buttonByText('New command')).toBeDefined();
    expect(editor()).not.toBeNull();
    expect(buttonByText('System commands')).toBeDefined();
  });

  it('opens the channel commands for a reader who asks for the system tab by address', async () => {
    held = ['commands:read'];
    await open('/app/commands?view=builtin');

    expect(document.body.querySelector('[data-builtin-row]')).toBeNull();
    expect(rowNames()).toContain('hello');
  });

  it('hides the first-command button in the empty list from a reader', async () => {
    held = ['commands:read'];
    rows = [];
    await open();

    expect(buttonByText('Make the first command')).toBeUndefined();
  });
});

const rowSwitch = (name: string): HTMLButtonElement | null =>
  rowEls()
    .find((row) => row.dataset.commandName === name)!
    .parentElement!.querySelector('button[role="switch"]');
const closeButton = (): HTMLButtonElement | null =>
  document.body.querySelector<HTMLButtonElement>('[data-detail-close]');

const pressEscape = async (target: EventTarget = document.body): Promise<void> => {
  target.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
  await settle();
};

describe('CommandsView closing the open command', () => {
  it('closes an open custom command from the close button, clearing the address', async () => {
    await open('/app/commands?command=hello');

    expect(closeButton()).not.toBeNull();
    closeButton()!.click();
    await settle();

    expect(router.currentRoute.value.query.command).toBeUndefined();
    expect(editor()).toBeNull();
  });

  it('closes an open built-in from the close button, clearing the address', async () => {
    await open('/app/commands?view=builtin&builtin=uptime');

    expect(document.body.querySelector('[data-builtin-detail]')).not.toBeNull();
    closeButton()!.click();
    await settle();

    expect(router.currentRoute.value.query.builtin).toBeUndefined();
    expect(router.currentRoute.value.query.view).toBe('builtin');
    expect(document.body.querySelector('[data-builtin-detail]')).toBeNull();
  });

  it('closes the open command on Escape, but leaves it when Escape was meant for the reply field', async () => {
    await open('/app/commands?command=hello');
    await pressEscape(reply());

    expect(router.currentRoute.value.query.command).toBe('hello');
    await pressEscape();

    expect(router.currentRoute.value.query.command).toBeUndefined();
    expect(editor()).toBeNull();
  });
});

describe('CommandsView built-in search', () => {
  it('filters the built-in list by name and says so when nothing matches', async () => {
    await open('/app/commands?view=builtin');
    const search = document.body.querySelector<HTMLInputElement>(
      '[data-slot="search-field"] input',
    )!;

    await type(search, 'shout');
    const titles = (): string[] =>
      [...document.body.querySelectorAll('[data-builtin-row]')].map(
        (row) => row.querySelector('[data-slot="summary-row-title"]')!.textContent!,
      );

    expect(titles()).toEqual(['!shoutout']);
    await type(search, '!UPT');
    expect(titles()).toEqual(['!uptime']);
    await type(search, 'zzz');
    expect(titles()).toEqual([]);
    expect(document.body.textContent).toContain('No command matches');
  });
});

describe('CommandsView switches in the All list', () => {
  it('switches a custom command with a PUT of isEnabled only', async () => {
    await open();

    expect(rowSwitch('hello')!.getAttribute('aria-checked')).toBe('true');
    expect(rowSwitch('socials')!.getAttribute('aria-checked')).toBe('false');
    rowSwitch('hello')!.click();
    await settle();

    expect(writes('PUT')).toHaveLength(1);
    expect(writes('PUT')[0]!.name).toBe('hello');
    expect(writes('PUT')[0]!.body).toEqual({ isEnabled: false });
  });

  it('gives an always-on system command no off switch and says it is always on', async () => {
    await open('/app/commands?view=builtin');

    const permit = builtinBody('!permit');

    expect(permit.parentElement!.querySelector('button[role="switch"]')).toBeNull();

    expect(
      permit.querySelector('[data-slot="summary-row-text"] > span:nth-child(2)')?.textContent,
    ).toBe('Always on · Moderator · 0s chat · 0s each');

    expect(builtinBody('!mydata').parentElement!.querySelector('button[role="switch"]')).toBeNull();
  });

  it('shows no switches to a reader', async () => {
    held = ['commands:read'];
    await open();

    expect(rowSwitch('hello')).toBeNull();
  });

  describe('on a phone', () => {
    beforeEach(() => {
      viewport = 390;
      stubMatchMedia();
    });

    const rowOf = (name: string): HTMLElement =>
      rowEls().find((row) => row.dataset.commandName === name)!;

    it('puts the Repeats and Off chips under the name so the switch column stays aligned', async () => {
      rows = [command('hello'), command('permit', { isEnabled: false })];
      await open();

      const permit = rowOf('permit');
      const chips = permit.querySelectorAll(
        '[data-slot="summary-row-meta"] [data-slot="status-badge"]',
      );

      expect(chips.length).toBe(2);

      const trailing = permit.parentElement!.querySelector('[data-slot="summary-row-trailing"]')!;

      expect(trailing.children.length).toBe(1);
      expect(trailing.querySelector('button[role="switch"]')).not.toBeNull();
      expect(trailing.querySelector('[data-slot="status-badge"]')).toBeNull();
    });

    it('keeps the chips beside the switch from the tablet width up', async () => {
      viewport = 1280;
      stubMatchMedia();
      rows = [command('permit')];
      await open();

      expect(rowOf('permit').querySelector('[data-slot="summary-row-meta"]')).toBeNull();
      expect(rowOf('permit').querySelector('[data-system-duplicate]')).not.toBeNull();
    });

    it('holds the sort and the search in one control row above the list, leaving only the scope tabs in the bar', async () => {
      await open();

      const controls = document.body.querySelector('[data-command-controls]')!;

      expect(controls.querySelector('[data-segmented]')).not.toBeNull();
      expect(controls.querySelector('[data-slot="search-field"] input')).not.toBeNull();
      expect(document.body.querySelector('[data-slot="page-bar"] [data-segmented]')).toBeNull();
      expect(document.body.querySelector('[data-slot="page-bar"] [role="tab"]')).not.toBeNull();
    });

    it('moves New command, the one primary action, into the sticky action footer', async () => {
      document.body.insertAdjacentHTML('beforeend', '<div data-slot="frame-action-footer"></div>');
      await open();

      const footer = document.body.querySelector('[data-slot="frame-action-footer"]')!;
      const button = [...footer.querySelectorAll('button')].find(
        (item) => item.textContent?.trim() === 'New command',
      )!;

      // No variant means the filled primary look.
      expect(button.getAttribute('data-variant') ?? 'default').toBe('default');

      expect(buttonByText('New command', document.body.querySelector('[data-slot="page-actions"]')!)).toBeUndefined();

      button.click();
      await settle();
      expect(document.body.querySelector('[role="dialog"]')).not.toBeNull();
    });
  });
});

describe('CommandsView code script reaction', () => {
  const script = (id: string, name: string, overrides: Row = {}) =>
    ({
      id,
      name,
      description: null,
      isEnabled: true,
      currentVersion: 1,
      currentValidationStatus: 'valid',
      ...overrides,
    }) as const satisfies Row;

  const pointer = (type: string): PointerEvent =>
    new PointerEvent(type, {
      bubbles: true,
      button: 0,
      pointerType: 'mouse',
    });

  // jsdom lacks these three; the select calls them when it opens.
  function stubPointerApis(): void {
    Element.prototype.hasPointerCapture = (): boolean => false;
    Element.prototype.releasePointerCapture = (): void => undefined;
    Element.prototype.scrollIntoView = (): void => undefined;
  }

  async function chooseScript(root: ParentNode, name: string): Promise<void> {
    stubPointerApis();
    root.querySelector<HTMLElement>('[data-code-script-select]')!.dispatchEvent(pointer('pointerdown'));
    await settle();

    const option = [...document.body.querySelectorAll<HTMLElement>('[role="option"]')].find((el) =>
      el.textContent?.includes(name),
    )!;

    option.dispatchEvent(pointer('pointerup'));
    option.click();
    await settle();
  }

  beforeEach(() => {
    scripts = [
      script('s1', 'Lucky feather', { description: 'Rolls a lucky feather for the viewer' }),
      script('s2', 'Dice'),
    ];
  });

  it('offers Code script as the fourth reply choice and shows a picker of the channel scripts', async () => {
    await open('/app/commands?command=hello');

    const labels = [...editor()!.querySelectorAll('[data-reply-mode] [data-segmented] button')].map(
      (button) => button.textContent?.trim(),
    );

    expect(labels).toEqual(['Text', 'Random reply', 'Code script', 'Pipeline']);
    expect(editor()!.querySelector('[data-code-script]')).toBeNull();

    buttonByText('Code script', editor()!).click();
    await settle();

    expect(editor()!.querySelector('textarea')).toBeNull();
    expect(editor()!.querySelector('[data-code-script-select]')).not.toBeNull();
    stubPointerApis();
    editor()!.querySelector<HTMLElement>('[data-code-script-select]')!.dispatchEvent(pointer('pointerdown'));
    await settle();

    const options = [...document.body.querySelectorAll('[role="option"]')].map((el) =>
      el.textContent?.trim(),
    );

    expect(options).toEqual(['Lucky feather', 'Dice']);
  });

  it('saves a command switched to Code script as the code tier with the script id and no pipeline id', async () => {
    rows[0] = command('hello', { pipelineId: 'p1' });

    await open('/app/commands?command=hello');
    buttonByText('Code script', editor()!).click();
    await settle();
    await chooseScript(editor()!, 'Lucky feather');
    expect(editor()!.textContent).toContain('Rolls a lucky feather for the viewer');

    buttonByText('Save changes').click();
    await settle();

    expect(writes('PUT')).toHaveLength(1);
    expect(writes('PUT')[0]!.body).toEqual({ tier: 'code', codeScriptId: 's1' });
  });

  it('says the channel has no scripts and links to the script page when the list is empty', async () => {
    scripts = [];

    await open('/app/commands?command=hello');
    buttonByText('Code script', editor()!).click();
    await settle();

    const empty = editor()!.querySelector('[data-code-script-empty]')!;

    expect(empty.textContent).toContain('no code scripts yet');
    expect(editor()!.querySelector('[data-code-script-select]')).toBeNull();
    expect(empty.querySelector('a')!.getAttribute('href')).toBe('/app/scripts');
  });

  it('opens an existing code command with its script selected and a link to that script', async () => {
    rows[2] = command('pipe', {
      tier: 'code',
      pipelineId: null,
      templateResponse: null,
      codeScriptId: 's2',
    });

    await open('/app/commands?command=pipe');

    const trigger = editor()!.querySelector<HTMLElement>('[data-code-script-select]')!;

    expect(trigger.textContent).toContain('Dice');
    expect(editor()!.querySelector('[data-code-script-link]')!.getAttribute('href')).toBe('/app/scripts?script=s2');
    expect(unsaved()).toBe('');
  });

  it('sends the new script when a code command is pointed at another script, and no other field', async () => {
    rows[2] = command('pipe', {
      tier: 'code',
      pipelineId: null,
      templateResponse: null,
      codeScriptId: 's2',
    });

    await open('/app/commands?command=pipe');
    await chooseScript(editor()!, 'Lucky feather');
    buttonByText('Save changes').click();
    await settle();

    expect(writes('PUT')[0]!.body).toEqual({ tier: 'code', codeScriptId: 's1' });
  });

  it('saves a code command switched to Text as the template tier', async () => {
    rows[2] = command('pipe', {
      tier: 'code',
      pipelineId: null,
      templateResponse: 'Back to text',
      codeScriptId: 's2',
    });

    await open('/app/commands?command=pipe');
    buttonByText('Text', editor()!).click();
    await settle();
    buttonByText('Save changes').click();
    await settle();

    expect(writes('PUT')[0]!.body).toEqual({ tier: 'template' });
  });

  it('creates a code command from the new command dialog with the chosen script and no pipeline id', async () => {
    await open();
    buttonByText('New command').click();
    await settle();

    const dialog = document.body.querySelector<HTMLElement>('[role="dialog"]')!;

    await type(document.body.querySelector<HTMLInputElement>('[data-new-name]')!, 'feather');
    stubPointerApis();
    dialog.querySelector<HTMLElement>('[role="combobox"]')!.dispatchEvent(pointer('pointerdown'));
    await settle();

    const kind = [...document.body.querySelectorAll<HTMLElement>('[role="option"]')].find((el) =>
      el.textContent?.includes('Runs a code script'),
    )!;

    kind.dispatchEvent(pointer('pointerup'));
    kind.click();
    await settle();

    await chooseScript(dialog, 'Dice');
    document.body.querySelector<HTMLButtonElement>('[data-form-action]')!.click();
    await settle();

    expect(writes('POST')).toHaveLength(1);

    expect(writes('POST')[0]!.body).toMatchObject({
      name: 'feather',
      tier: 'code',
      codeScriptId: 's2',
    });

    expect(writes('POST')[0]!.body).not.toHaveProperty('pipelineId');
  });

  it('keeps the new command dialog open and asks for a script when none is chosen', async () => {
    await open();
    buttonByText('New command').click();
    await settle();

    const dialog = document.body.querySelector<HTMLElement>('[role="dialog"]')!;

    await type(document.body.querySelector<HTMLInputElement>('[data-new-name]')!, 'feather');
    stubPointerApis();
    dialog.querySelector<HTMLElement>('[role="combobox"]')!.dispatchEvent(pointer('pointerdown'));
    await settle();

    const kind = [...document.body.querySelectorAll<HTMLElement>('[role="option"]')].find((el) =>
      el.textContent?.includes('Runs a code script'),
    )!;

    kind.dispatchEvent(pointer('pointerup'));
    kind.click();
    await settle();
    document.body.querySelector<HTMLButtonElement>('[data-form-action]')!.click();
    await settle();

    expect(writes('POST')).toHaveLength(0);
    expect(document.body.querySelector('[data-form-error]')?.textContent).toContain('Choose a script');
  });

  it('names a code command in the list as a Code script', async () => {
    rows[2] = command('pipe', {
      tier: 'code',
      pipelineId: null,
      templateResponse: null,
      codeScriptId: 's2',
    });

    await open('/app/commands?view=custom');

    const row = rowEls().find((el) => el.dataset.commandName === 'pipe')!;

    expect(row.textContent).toContain('Code script · 0 uses');
  });
});
