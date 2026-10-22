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

import en from '../i18n/en.json';
import LexiconPanel from '../LexiconPanel.vue';

type Rule = {
  id: string;
  phrase: string;
  replacement: string;
  matchKind: string;
};

type Sent = {
  method: string;
  path: string;
  entryId: string | undefined;
  body: Omit<Rule, 'id'>;
};

const sent: Sent[] = [];
let rules: Rule[] = [];
let refuseWith: number | null = null;
let allowed = true;

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => allowed }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

type Init = {
  params?: { path?: { entryId?: string } };
  body: Omit<Rule, 'id'>;
};

function write(method: string) {
  return async (path: string, init: Init): Promise<unknown> => {
    sent.push({
      method,
      path,
      entryId: init.params?.path?.entryId,
      body: init.body,
    });

    if (refuseWith !== null) {
      return {
        data: undefined,
        error: { detail: 'That rule exists.' },
        response: { status: refuseWith, ok: false },
      };
    }

    if (method === 'POST') {
      rules = [...rules, { id: 'e2', ...init.body }];
    } else {
      rules = rules.map((rule) =>
        rule.id === init.params?.path?.entryId
          ? { id: rule.id, ...init.body }
          : rule);
    }

    return { data: { data: null }, response: { status: 200, ok: true } };
  };
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string): Promise<unknown> => ({
      data: { data: path.endsWith('/tts/lexicon')
        ? rules
        : null },
      response: { status: 200, ok: true },
    }),
    POST: (path: string, init: Init) => write('POST')(path, init),
    PUT: (path: string, init: Init) => write('PUT')(path, init),
    DELETE: () => Promise.resolve({ response: { status: 200, ok: true } }),
  },
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  sent.length = 0;
  refuseWith = null;
  allowed = true;

  rules = [
    {
      id: 'e1',
      phrase: 'gg',
      replacement: 'good game',
      matchKind: 'word',
    },
  ];

  pinia = createPinia();
  setActivePinia(pinia);
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function settle(): Promise<void> {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
}

async function open(): Promise<void> {
  mounted = mount(LexiconPanel, {
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

const dialog = (): HTMLElement | null =>
  document.body.querySelector<HTMLElement>('[data-lexicon-dialog]');
const field = (id: string): HTMLInputElement =>
  document.body.querySelector<HTMLInputElement>(`#${id}`) as HTMLInputElement;
const control = (selector: string): HTMLButtonElement =>
  document.body.querySelector<HTMLButtonElement>(selector) as HTMLButtonElement;
const rows = (): string[] =>
  [...document.body.querySelectorAll('[data-lexicon-row]')].map(
    (row) => row.textContent?.replace(/\s+/g, ' ').trim() ?? '',
  );

async function type(id: string, value: string): Promise<void> {
  const input = field(id);

  input.value = value;
  input.dispatchEvent(new Event('input'));
  await flushPromises();
}

async function click(target: HTMLElement): Promise<void> {
  target.click();
  await settle();
}

const addButton = (): HTMLButtonElement => control('[data-lexicon-add]');
const editButton = (): HTMLButtonElement =>
  control('[aria-label="Edit the rule for gg"]');

describe('Pronunciation rule dialog', () => {
  it('has no inline form on the panel any more', async () => {
    await open();

    expect(document.body.querySelector('[data-view="tts-lexicon"] form')).toBeNull();
    expect(dialog()).toBeNull();
  });

  it('adds a rule through the dialog, posts it and lists it', async () => {
    await open();
    await click(addButton());

    expect(dialog()?.textContent).toContain('Add a pronunciation rule');
    expect(field('lexicon-phrase').value).toBe('');

    await type('lexicon-phrase', 'lol');
    await type('lexicon-replacement', 'laughing out loud');
    await click(control('[data-form-action]'));

    expect(sent).toHaveLength(1);
    expect(sent[0]!.method).toBe('POST');
    expect(sent[0]!.path).toContain('/tts/lexicon');

    expect(sent[0]!.body).toEqual({
      phrase: 'lol',
      replacement: 'laughing out loud',
      matchKind: 'word',
    });

    expect(dialog()).toBeNull();
    expect(rows()).toHaveLength(2);
    expect(rows()[1]).toContain('laughing out loud');
  });

  it('opens the edit dialog filled with the current values of that rule', async () => {
    await open();
    await click(editButton());

    expect(dialog()?.textContent).toContain('Edit the pronunciation rule');
    expect(field('lexicon-phrase').value).toBe('gg');
    expect(field('lexicon-replacement').value).toBe('good game');
    expect(dialog()?.textContent).toContain('Whole word, any case');
  });

  it('edits a rule through the dialog: a PUT to its id with the changed fields, then the new text in the list', async () => {
    await open();
    await click(editButton());
    await type('lexicon-replacement', 'great game');
    await click(control('[data-form-action]'));

    expect(sent).toHaveLength(1);
    expect(sent[0]!.method).toBe('PUT');
    expect(sent[0]!.path).toContain('/tts/lexicon/{entryId}');
    expect(sent[0]!.entryId).toBe('e1');

    expect(sent[0]!.body).toEqual({
      phrase: 'gg',
      replacement: 'great game',
      matchKind: 'word',
    });

    expect(dialog()).toBeNull();
    expect(rows()).toHaveLength(1);
    expect(rows()[0]).toContain('great game');
    expect(rows()[0]).not.toContain('good game');
  });

  it('opens an add dialog empty after an edit dialog was canceled', async () => {
    await open();
    await click(editButton());
    await click(control('[data-form-cancel]'));
    await click(addButton());

    expect(field('lexicon-phrase').value).toBe('');
    expect(field('lexicon-replacement').value).toBe('');
  });

  it('closes on Cancel and on Escape without sending a request', async () => {
    await open();
    await click(editButton());
    await click(control('[data-form-cancel]'));
    expect(dialog()).toBeNull();

    await click(addButton());
    expect(dialog()).not.toBeNull();

    dialog()?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();

    expect(dialog()).toBeNull();
    expect(sent).toHaveLength(0);
  });

  it('keeps the dialog open with the duplicate message when the bot refuses the save', async () => {
    refuseWith = 409;
    await open();
    await click(editButton());
    await type('lexicon-replacement', 'great game');
    await click(control('[data-form-action]'));

    expect(sent).toHaveLength(1);
    expect(dialog()).not.toBeNull();
    expect(control('[data-form-error]').textContent).toContain(en.dashboard.tts.lexicon.duplicate);
    expect(field('lexicon-replacement').value).toBe('great game');
    expect(rows()[0]).toContain('good game');
  });

  it('keeps the save off until a phrase and a replacement are typed', async () => {
    await open();
    await click(addButton());

    expect(control('[data-form-action]').disabled).toBe(true);

    await type('lexicon-phrase', 'lol');
    expect(control('[data-form-action]').disabled).toBe(true);

    await type('lexicon-replacement', 'laughing out loud');
    expect(control('[data-form-action]').disabled).toBe(false);
  });

  it('limits the phrase to 100 and the replacement to 200 characters', async () => {
    await open();
    await click(addButton());

    expect(field('lexicon-phrase').maxLength).toBe(100);
    expect(field('lexicon-replacement').maxLength).toBe(200);
  });

  it('shows no add or edit control to a viewer without tts:config:write', async () => {
    allowed = false;
    await open();

    expect(document.body.querySelector('[data-lexicon-add]')).toBeNull();
    expect(document.body.querySelector('[aria-label="Edit the rule for gg"]')).toBeNull();
    expect(rows()).toHaveLength(1);
  });
});
