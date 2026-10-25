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

// What the bot says it can post for. The page lists these and nothing it holds a copy of.
const offeredTriggers = [
  {
    name: 'go_live',
    labelKey: 'dashboard.discord.triggers.go_live',
    placeholders: ['broadcaster', 'channel.name', 'title', 'game'],
  },
  {
    name: 'follow',
    labelKey: 'dashboard.discord.triggers.follow',
    placeholders: ['user.name', 'channel.display'],
  },
  {
    name: 'raid',
    labelKey: 'dashboard.discord.triggers.raid',
    placeholders: ['user.name', 'viewers', 'channel.display'],
  },
];

// What the preview endpoint answers for the embed; a test sets it to the rendered embed it wants shown.
let previewEmbed: unknown = null;

function answer(path: string): unknown {
  if (path.endsWith('/discord/triggers'))
    return ok(offeredTriggers);

  if (path.endsWith('/discord/connections'))
    return ok([connection]);

  if (path.endsWith('/configs'))
    return ok([goLive]);

  if (path.endsWith('/preview'))
    return ok({
      renderedContent: 'SampleStreamer is live',
      renderedEmbed: previewEmbed,
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
  previewEmbed = null;
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

    // One row per trigger the bot lists, in its order. Go-live carries the stored rule's state.
    const edits = rows.map((row) => row.querySelector('[data-edit]')?.getAttribute('data-edit'));

    expect(edits).toEqual(['go_live', 'follow', 'raid']);
    expect(rows[0]?.textContent).toContain('Stream goes live');
    expect(rows[0]?.textContent).toContain('On');
    expect(rows[1]?.textContent).toContain('New follower');
    expect(rows[1]?.textContent).toContain('Not set up');
    expect(rows[2]?.textContent).toContain('Incoming raid');
  });

  it('does not offer a trigger the bot did not list, such as new clip', async () => {
    mounted = mount(DiscordView, { attachTo: document.body, global: { plugins: plugins() } });
    await settle();

    expect(document.body.querySelector('[data-edit="new_clip"]')).toBeNull();
    expect(document.body.querySelector('[data-edit="milestone"]')).toBeNull();
  });
});

describe('the rule editor', () => {
  async function editor(rule: unknown = goLive): Promise<void> {
    mounted = mount(RuleEditor, {
      attachTo: document.body,
      props: {
        trigger: 'go_live',
        placeholders: [],
        rule: rule as never,
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

  it('suggests placeholders in every embed text field, not only the message', async () => {
    await editor();

    for (const field of [
      'embed-title',
      'embed-description',
      'embed-thumbnail',
      'embed-image',
      'embed-footer',
      'embed-title-url',
      'embed-author-name',
      'embed-author-icon',
      'embed-author-url',
      'embed-footer-icon',
      'embed-button-label',
      'embed-button-url',
    ]) {
      expect(
        `${field}: ${document.body.querySelector(`[data-field="${field}"]`)?.getAttribute('aria-autocomplete')}`,
      ).toBe(`${field}: list`);
    }
  });

  it('saves the edited embed title with the rule shape the API expects', async () => {
    await editor();

    const title = document.body.querySelector<HTMLTextAreaElement>('[data-field="embed-title"]');

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

  async function type(field: string, value: string): Promise<void> {
    const input = document.body.querySelector<HTMLTextAreaElement>(`[data-field="${field}"]`);

    input!.value = value;
    input!.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
  }

  it('saves the author, links, footer icon, time flag and button with the rule shape the API expects', async () => {
    await editor();

    await type('embed-title-url', '{channel.url}');
    await type('embed-author-name', '{channel.name} is now live on Twitch!');
    await type('embed-author-icon', '{channel.avatar}');
    await type('embed-author-url', '{channel.url}');
    await type('embed-footer-icon', '{channel.avatar}');
    await type('embed-button-label', 'Watch Stream');
    await type('embed-button-url', '{channel.url}');
    // The first switch is the rule's on/off; the second is the embed's time.
    document.body.querySelectorAll<HTMLButtonElement>('[role="switch"]')[1]?.click();
    await settle();
    document.body.querySelector<HTMLButtonElement>('[data-save-rule]')!.click();
    await settle();

    expect(calls).toHaveLength(1);

    expect(calls[0]?.body).toMatchObject({
      embedConfig: {
        title: 'Live now',
        titleUrl: '{channel.url}',
        authorName: '{channel.name} is now live on Twitch!',
        authorIconUrl: '{channel.avatar}',
        authorUrl: '{channel.url}',
        footerIconUrl: '{channel.avatar}',
        showTimestamp: true,
        buttonLabel: 'Watch Stream',
        buttonUrl: '{channel.url}',
      },
    });
  });

  it('saves no embed at all when every part is empty, even with the time switch left off', async () => {
    await editor({ ...goLive, embedConfig: null });

    document.body.querySelector<HTMLButtonElement>('[data-save-rule]')!.click();
    await settle();

    expect(calls[0]?.body).toMatchObject({ embedConfig: null });
  });

  it('shows the rendered author, linked title, fields, image, footer, time and button in the preview', async () => {
    previewEmbed = {
      title: 'blame the lag',
      titleUrl: 'https://www.twitch.tv/stoney',
      description: null,
      color: '#ff0000',
      imageUrl: 'https://img.example/preview.jpg',
      authorName: 'Stoney is now live on Twitch!',
      authorIconUrl: 'https://img.example/avatar.png',
      authorUrl: 'https://www.twitch.tv/stoney',
      footerText: 'NomNomzBot',
      showTimestamp: true,
      buttonLabel: 'Watch Stream',
      buttonUrl: 'https://www.twitch.tv/stoney',
      fields: [
        {
          name: 'Game',
          value: 'Just Chatting',
          inline: true,
        },
      ],
    };

    await editor();

    document.body.querySelector<HTMLButtonElement>('[data-preview-button]')!.click();
    await settle();

    const card = document.body.querySelector<HTMLElement>('[data-preview-embed]');

    expect(card?.style.getPropertyValue('--embed-color')).toBe('#ff0000');

    expect(card?.querySelector('[data-preview-author]')?.textContent).toContain(
      'Stoney is now live',
    );

    expect(card?.querySelector('[data-preview-title] a')?.getAttribute('href')).toBe(
      'https://www.twitch.tv/stoney',
    );

    expect(card?.querySelector('[data-preview-field]')?.textContent).toContain('Just Chatting');

    expect(card?.querySelector('[data-preview-image]')?.getAttribute('src')).toBe(
      'https://img.example/preview.jpg',
    );

    expect(card?.querySelector('[data-preview-footer]')?.textContent).toContain('NomNomzBot');

    expect(card?.querySelector('[data-preview-timestamp]')?.textContent).toContain(
      'Sent just now',
    );

    expect(card?.querySelector('[data-preview-button-link]')?.textContent).toContain(
      'Watch Stream',
    );
  });

  it('draws no color stripe variable when the embed color is not a hex color', async () => {
    previewEmbed = { title: 'x', color: 'red' };
    await editor();

    document.body.querySelector<HTMLButtonElement>('[data-preview-button]')!.click();
    await settle();

    const card = document.body.querySelector<HTMLElement>('[data-preview-embed]');

    expect(card).not.toBeNull();
    expect(card?.style.getPropertyValue('--embed-color')).toBe('');
  });
});

describe('a new rule on the rule editor', () => {
  it('starts on the fitting channel with a ready message, so one press of save is enough', async () => {
    mounted = mount(RuleEditor, {
      attachTo: document.body,
      props: {
        trigger: 'go_live',
        placeholders: [],
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
      messageTemplate:
        '{channel.name} is live on Twitch streaming {game}, {title}. Come hang out: {channel.url}',
      embedConfig: {
        title: '{title}',
        titleUrl: '{channel.url}',
        authorName: '{channel.name} is now live on Twitch!',
        authorIconUrl: '{channel.avatar}',
        imageUrl: '{stream.thumbnail}',
        fields: [
          {
            name: 'Game',
            value: '{game}',
            inline: true,
          },
        ],
        showTimestamp: true,
        buttonLabel: 'Watch Stream',
        buttonUrl: '{channel.url}',
      },
    });
  });

  it.each([
    ['follow', '{user.name} just followed {channel.display}!'],
    ['subscription', '{user.name} just joined {channel.display} as a supporter ({tier})!'],
    ['gift_subscription', '{user.name} gifted {count} memberships in {channel.display} ({tier})!'],
    ['cheer', '{user.name} cheered {bits} bits in {channel.display}! {message}'],
    ['raid', '{user.name} raided {channel.display} with {viewers} viewers!'],
    ['stream_end', '{channel.name} is offline. The stream ran for {duration}.'],
    [
      'resubscription',
      '{user.name} renewed their support of {channel.display} ({tier}) for {months} months in total! {message}',
    ],
    [
      'poll_end',
      'The poll is over: {poll.title} The winner is {poll.winner} with {poll.winner.votes} votes.',
    ],
    [
      'prediction_resolved',
      'The prediction is resolved: {prediction.title} The winning outcome is {prediction.winner}.',
    ],
  ])('starts a %s rule with its own ready message', async (trigger, message) => {
    mounted = mount(RuleEditor, {
      attachTo: document.body,
      props: {
        trigger,
        placeholders: ['user.name', 'channel.display'],
        rule: null,
        connectionId: 'conn1',
        pickerError: null,
        channels: [
          {
            id: 'a',
            name: 'alerts',
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

    document.body.querySelector<HTMLButtonElement>('[data-save-rule]')!.click();
    await settle();

    expect(calls[0]?.body).toMatchObject({
      triggerType: trigger,
      messageTemplate: message,
      embedConfig: null,
    });
  });

  it('lists the placeholders the bot fills for the trigger', async () => {
    mounted = mount(RuleEditor, {
      attachTo: document.body,
      props: {
        trigger: 'raid',
        placeholders: ['user.name', 'viewers', 'channel.display'],
        rule: null,
        connectionId: 'conn1',
        pickerError: null,
        channels: [],
        roles: [],
      },
      global: { plugins: plugins() },
    });

    await settle();

    expect(document.body.querySelector('[data-field="placeholders"]')?.textContent).toContain(
      '{user.name}, {viewers}, {channel.display}',
    );
  });
});
