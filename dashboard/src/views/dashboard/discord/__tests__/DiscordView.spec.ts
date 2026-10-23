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
import { ref, type Plugin } from 'vue';

import { i18n } from '@/i18n';

import DiscordView from '../DiscordView.vue';
import RuleEditor from '../components/RuleEditor.vue';
import en from '../i18n/en.json';

type Options = { params?: { path?: Record<string, unknown> }; body?: unknown };
type Call = {
  method: string;
  path: string;
  body: unknown;
};

const calls: Call[] = [];
const ok = (data: unknown): unknown => ({ data: { data }, response: { status: 200, ok: true } });

const connection = {
  id: 'conn1',
  broadcasterId: 'c1',
  guildId: 'g1',
  guildName: 'Stoney Guild',
  botInstalled: true,
  serverConsentStatus: 'approved',
  streamerEnabled: true,
  isLinkActive: true,
};

const goLive = {
  id: 'rule1',
  guildConnectionId: 'conn1',
  triggerType: 'go_live',
  enabled: true,
  targetChannelId: 'ch1',
  pingRoleId: null,
  messageTemplate: '{channel.name} is live',
  embedConfig: {
    title: 'Live now',
    fields: [
      {
        name: 'Game',
        value: '{channel.game}',
        inline: false,
      },
    ],
  },
  milestoneType: null,
  milestoneThreshold: null,
};

function answer(path: string): unknown {
  if (path.endsWith('/discord/connections'))
    return ok([connection]);

  if (path.endsWith('/configs'))
    return ok([goLive]);

  if (path.endsWith('/preview'))
    return ok({
      renderedContent: 'SampleStreamer is live',
      renderedEmbed: null,
      pingRoleMention: null,
    });

  if (path.endsWith('/guild/channels/postable'))
    return ok([
      {
        id: 'ch1',
        name: 'live',
        type: 0,
        position: 0,
        canPost: true,
      },
    ]);

  return ok([]);
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string) => answer(path),
    PUT: async (path: string, options?: Options) => {
      calls.push({
        method: 'PUT',
        path,
        body: options?.body,
      });

      return ok(goLive);
    },
    POST: async (path: string, options?: Options) => {
      calls.push({
        method: 'POST',
        path,
        body: options?.body,
      });

      return ok(goLive);
    },
  },
}));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: ref(true), can: (): boolean => true }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ channelId: ref('c1') }),
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

beforeEach(() => {
  calls.length = 0;
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

const plugins = (): Array<Plugin | [Plugin, ...unknown[]]> => [
  i18n,
  pinia,
  [
    VueQueryPlugin,
    { queryClient: new QueryClient({ defaultOptions: { queries: { retry: false } } }) },
  ],
];

describe('the Discord page', () => {
  it('shows the linked server as ready and lists go-live first with its stored state', async () => {
    mounted = mount(DiscordView, { attachTo: document.body, global: { plugins: plugins() } });
    await settle();

    const rows = [...document.body.querySelectorAll('[data-rule-row]')];

    expect(document.body.querySelector('[data-connection]')?.textContent).toContain('Stoney Guild');

    expect(document.body.querySelector('[data-connection]')?.textContent).toContain(
      'Linked and ready',
    );

    // Only the trigger the bot posts for by itself is offered, under the name the server saves it with.
    expect(rows).toHaveLength(1);
    expect(rows[0]?.textContent).toContain('Stream goes live');
    expect(rows[0]?.textContent).toContain('On');
  });
});

describe('the rule editor', () => {
  async function editor(): Promise<void> {
    mounted = mount(RuleEditor, {
      attachTo: document.body,
      props: {
        trigger: 'go_live',
        rule: goLive as never,
        connectionId: 'conn1',
        pickerError: null,
        channels: [
          {
            id: 'ch1',
            name: 'live',
            type: 0,
            position: 0,
            canPost: true,
          },
        ],
        roles: [],
      },
      global: { plugins: plugins() },
    });

    await settle();
  }

  it('saves the edited embed title with the rule shape the API expects', async () => {
    await editor();

    const title = document.body.querySelector<HTMLInputElement>('[data-field="embed-title"]');

    title!.value = 'We are live';
    title!.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
    document.body.querySelector<HTMLButtonElement>('[data-save-rule]')!.click();
    await settle();

    expect(calls).toHaveLength(1);
    expect(calls[0]?.path).toContain('/discord/configs/{configId}');

    expect(calls[0]?.body).toMatchObject({
      enabled: true,
      targetChannelId: 'ch1',
      pingRoleId: null,
      messageTemplate: '{channel.name} is live',
      embedConfig: {
        title: 'We are live',
        fields: [
          {
            name: 'Game',
            value: '{channel.game}',
            inline: false,
          },
        ],
      },
    });
  });

  it('saves first and then shows the rendered preview', async () => {
    await editor();

    document.body.querySelector<HTMLButtonElement>('[data-preview-button]')!.click();
    await settle();

    expect(calls.map((call) => call.method)).toEqual(['PUT']);

    expect(document.body.querySelector('[data-preview-content]')?.textContent).toContain(
      'SampleStreamer is live',
    );
  });
});

describe('a new rule on the rule editor', () => {
  it('starts on the fitting channel with a ready message, so one press of save is enough', async () => {
    mounted = mount(RuleEditor, {
      attachTo: document.body,
      props: {
        trigger: 'go_live',
        rule: null,
        connectionId: 'conn1',
        pickerError: null,
        channels: [
          {
            id: 'g',
            name: 'general',
            type: 0,
            position: 0,
            canPost: true,
          },
          {
            id: 'ls',
            name: 'live-streams',
            type: 0,
            position: 1,
            canPost: true,
          },
        ],
        roles: [],
      },
      global: { plugins: plugins() },
    });

    await settle();

    document.body.querySelector<HTMLButtonElement>('[data-save-rule]')!.click();
    await settle();

    expect(calls).toHaveLength(1);
    expect(calls[0]?.method).toBe('POST');

    expect(calls[0]?.body).toMatchObject({
      enabled: true,
      targetChannelId: 'ls',
      messageTemplate: '{channel.name} is live! {stream.title} ({stream.game})',
    });
  });
});
