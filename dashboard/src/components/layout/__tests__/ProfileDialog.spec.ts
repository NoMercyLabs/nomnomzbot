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
import { computed, defineComponent, ref } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { onTheWire } from '@/api/__tests__/wire';
import ProfileDialog from '@/components/layout/ProfileDialog.vue';
import UserName from '@/components/layout/UserName.vue';
import { i18n } from '@/i18n';
import { merge } from '@/lib/array';
import { useProfileDialogStore } from '@/stores/profileDialog';
import { useSaveErrorsStore } from '@/stores/saveErrors';

type Call = {
  path: string;
  params: Record<string, unknown> | undefined;
  body: unknown;
};

const calls: Call[] = [];
let vipWrite: Promise<unknown> | null = null;
let vipWriteFails = false;

// Fresh per test: a VIP save changes the stored person, as the server's record would.
let people: Record<string, Record<string, unknown>> = {};

const freshPeople = (): Record<string, Record<string, unknown>> => ({
  u1: {
    id: 'u1',
    internalUserId: 'g1',
    username: 'ada',
    displayName: 'Ada',
    profileImageUrl: null,
    messageCount: 1234,
    watchHours: 12.5,
    commandsUsed: 7,
    trustLevel: 'viewer',
    isBanned: false,
    isVip: false,
    isModerator: false,
    firstSeen: '2026-01-02T10:00:00Z',
    lastSeen: '2026-10-01T10:00:00Z',
    recentActivity: [
      {
        type: 'chat',
        content: 'hello from ada',
        timestamp: '2026-10-01T10:00:00Z',
      },
      {
        type: 'chat',
        content: 'second line from ada',
        timestamp: '2026-10-01T10:01:00Z',
      },
    ],
    banHistory: [],
  },
  u2: {
    id: 'u2',
    internalUserId: 'g2',
    username: 'bo',
    displayName: 'Bo',
    profileImageUrl: null,
    messageCount: 5,
    watchHours: 0.5,
    commandsUsed: 0,
    trustLevel: 'moderator',
    isBanned: true,
    isVip: null,
    isModerator: null,
    firstSeen: '2026-02-02T10:00:00Z',
    lastSeen: '2026-09-01T10:00:00Z',
    recentActivity: [],
    banHistory: [
      {
        id: 'b1',
        bannedBy: 'Stoney_Eagle',
        reason: 'spam links',
        bannedAt: '2026-09-01T10:00:00Z',
        unbannedAt: null,
      },
      {
        id: 'b0',
        bannedBy: 'Stoney_Eagle',
        reason: 'first warning ignored',
        bannedAt: '2026-08-01T10:00:00Z',
        unbannedAt: '2026-08-03T10:00:00Z',
      },
    ],
  },
});

// The moderator notes by Twitch id (what GET moderation/users/{userId}/notes answers); fresh per test.
let notes: Record<string, Record<string, unknown>[]> = {};

const freshNotes = (): Record<string, Record<string, unknown>[]> => ({
  u1: [
    {
      id: 7,
      subjectUserId: 'u1',
      content: 'Helps new chatters find the rules.',
      pinned: true,
      authorName: 'Stoney_Eagle',
      createdAt: '2026-09-20T10:00:00Z',
      updatedAt: '2026-09-20T10:00:00Z',
    },
  ],
});

// The moderation history pages by internal id (what GET moderation/history/{userId} answers), newest first.
const history: Record<string, Record<string, unknown>[][]> = {
  g1: [
    [
      {
        id: 'h2',
        subjectUserId: 'g1',
        subjectTwitchUserId: 'u1',
        actionType: 'timeout',
        moderatorUserId: 'm1',
        moderatorDisplayName: 'Stoney_Eagle',
        reason: 'Spam links',
        durationSeconds: 600,
        occurredAt: '2026-10-01T18:00:00Z',
      },
      {
        id: 'h1',
        subjectUserId: 'g1',
        subjectTwitchUserId: 'u1',
        actionType: 'filter_hit',
        occurredAt: '2026-09-30T18:00:00Z',
      },
    ],
    [
      {
        id: 'h0',
        subjectUserId: 'g1',
        subjectTwitchUserId: 'u1',
        actionType: 'warn',
        moderatorUserId: 'm1',
        moderatorDisplayName: 'Stoney_Eagle',
        reason: 'Caps',
        occurredAt: '2026-09-01T18:00:00Z',
      },
    ],
  ],
};

// A person's whole chat by internal id, newest page first. The cursor is the index of the next page.
let chatPages: Record<string, Record<string, unknown>[][]> = {};

function chatLine(id: string, message: string, timestamp: string): Record<string, unknown> {
  return {
    id,
    message,
    timestamp,
  };
}

function chatPage(options: Options | undefined): unknown {
  const pages = chatPages[userIdOf(options)] ?? [[]];
  const before = (options?.params?.query as Record<string, unknown> | undefined)?.before;
  const index = before === undefined
    ? 0
    : Number(before);

  return {
    data: {
      data: {
        messages: onTheWire(pages[index] ?? []),
        nextCursor: index + 1 < pages.length
          ? String(index + 1)
          : null,
      },
    },
    response: { ok: true, status: 200 },
  };
}

function historyPage(options: Options | undefined): unknown {
  const pages = history[userIdOf(options)] ?? [[]];
  const page = Number((options?.params?.query as Record<string, unknown> | undefined)?.Page ?? 1);

  return {
    data: onTheWire(pages[page - 1] ?? []),
    hasMore: page < pages.length,
    nextPage: page < pages.length
      ? page + 1
      : null,
  };
}

// A person's own channel emotes by internal id (the route only takes the internal Guid).
const emotes: Record<string, Record<string, unknown>[]> = {
  g1: [
    {
      code: 'adaHi',
      provider: 'Twitch',
      urls: { 1: 'https://cdn/adaHi/1', 3: 'https://cdn/adaHi/3' },
      animated: false,
      zeroWidth: false,
      setId: 's1',
      id: 'e1',
      type: 'subscriptions',
      tier: 1,
      origin: 'other',
      ownerId: 'u1',
      ownerLogin: 'ada',
      ownerName: 'Ada',
      ownerAvatarUrl: null,
      staticUrls: { 1: 'https://cdn/adaHi/1', 3: 'https://cdn/adaHi/3' },
    },
    {
      code: 'adaDance',
      provider: 'SevenTv',
      urls: { 1: 'https://7tv/adaDance/1', 4: 'https://7tv/adaDance/4' },
      animated: true,
      zeroWidth: false,
      setId: null,
      id: '7d',
      type: null,
      tier: null,
      origin: 'other',
      ownerId: 'u1',
      ownerLogin: 'ada',
      ownerName: 'Ada',
      ownerAvatarUrl: null,
      staticUrls: { 1: 'https://7tv/adaDance/1s', 4: 'https://7tv/adaDance/4s' },
    },
  ],
};

// The viewer profile by internal id (what GET community/{userId}/profile answers).
let profiles: Record<string, Record<string, unknown> | null> = {};

// The sections the live API always sends, here for a person the bot stored nothing extra about.
const storedNothing = (): Record<string, unknown> => ({
  economy: {
    wallet: null,
    giveawayEntryCount: 0,
    giveawayWinCount: 0,
    leaderboardOptedOut: false,
  },
  permits: {
    activePermits: [],
    ageConsentGranted: null,
    ageConsentConfirmedUtc: null,
  },
  overrides: { shoutoutMessageTemplate: null, ttsVoice: null },
  commandUsage: { totalCommandsUsed: 0, lastUsedUtc: null },
  raids: { timesRaidedThisChannel: 0, timesThisChannelRaidedThem: 0 },
  freeFormData: [],
  recentQuotes: [],
  totalQuoteCount: 0,
});

const freshProfiles = (): Record<string, Record<string, unknown> | null> => ({
  g1: {
    ...storedNothing(),
    identity: {
      userId: 'g1',
      twitchUserId: '90210',
      username: 'ada',
      displayName: 'Ada',
      profileImageUrl: null,
      pronoun: 'she/her',
      altPronoun: null,
      linkedIdentities: [
        {
          provider: 'Kick',
          providerUsername: 'ada_k',
          providerDisplayName: 'AdaK',
          providerAvatarUrl: null,
          isPrimary: false,
          linkedAtUtc: '2026-05-06T00:00:00Z',
          lastLoginAtUtc: null,
        },
      ],
      communityStanding: 'Everyone',
      // The live API sends the Twitch tier code, never a label.
      subTier: '1000',
      managementRole: 'Moderator',
      memberSinceUtc: '2020-03-04T00:00:00Z',
      firstSeenUtc: '2026-01-02T10:00:00Z',
      accountCreatedAtUtc: '2015-06-01T00:00:00Z',
      followedAtUtc: '2024-08-15T00:00:00Z',
      description: 'Writes compilers for fun.',
      broadcasterType: 'affiliate',
      userType: 'staff',
      chatColor: '#FF7F50',
      nickname: 'Countess',
      timezone: 'Europe/London',
      isLurking: true,
      pronounIsManualOverride: true,
    },
    activity: {
      totalMessages: 1234,
      totalRedemptions: 9,
      totalSongRequests: 2,
      isFollower: true,
      isSubscriber: true,
      subTier: '1000',
      totalBitsCheered: 500,
    },
    streak: {
      currentStreak: 4,
      maxStreak: 6,
      lastSeenDate: '2026-10-01',
    },
    raids: { timesRaidedThisChannel: 3, timesThisChannelRaidedThem: 1 },
  },
  g2: {
    ...storedNothing(),
    identity: {
      userId: 'g2',
      twitchUserId: null,
      username: 'bo',
      displayName: 'Bo',
      profileImageUrl: null,
      pronoun: null,
      altPronoun: null,
      linkedIdentities: [],
      communityStanding: 'Everyone',
      subTier: null,
      managementRole: null,
      memberSinceUtc: null,
      firstSeenUtc: '2026-02-02T10:00:00Z',
      accountCreatedAtUtc: null,
      description: null,
      broadcasterType: '',
      userType: '',
      chatColor: null,
      nickname: null,
      timezone: null,
      isLurking: false,
      pronounIsManualOverride: false,
    },
    activity: null,
    streak: null,
  },
});

// The action keys a full moderator holds; a test drops one to see its control lock.
const MODERATOR_KEYS = [
  'community:read',
  'moderation:ban',
  'moderation:unban',
  'moderation:vip',
  'moderation:shoutout',
  'moderation:usercontext:read',
  'moderation:note:write',
  'moderation:history:read',
  'moderation:warn',
  'moderation:suspicioususer:write',
] as const;

// The bot-side standing by Twitch id (what GET moderation/users/{userId}/context carries); fresh per test.
let standings: Record<string, Record<string, unknown> | undefined> = {};

// The server notes every standing change on the person, as a system note.
function noteStanding(userId: string, text: string): void {
  notes[userId] = [
    {
      id: 90 + (notes[userId]?.length ?? 0),
      subjectUserId: userId,
      content: text,
      pinned: false,
      authorName: 'Stoney_Eagle',
      createdAt: '2026-10-04T10:00:00Z',
      updatedAt: '2026-10-04T10:00:00Z',
    },
    ...(notes[userId] ?? []),
  ];
}

// A person's own TTS voice id by Twitch id; a person without one gets the 404 the bot sends.
let personVoices: Record<string, string | undefined> = {};

const voiceOf = (id: string, displayName: string): Record<string, unknown> => ({
  id,
  name: id,
  displayName,
  locale: 'en-US',
  gender: 'Female',
  provider: 'Edge',
  isDefault: false,
  styles: [],
  tags: [],
});

const catalog = [voiceOf('en-US-AvaNeural', 'Ava'), voiceOf('en-US-EmmaNeural', 'Emma')];

function voicesPage(options: Options | undefined): unknown {
  const q = String((options?.params?.query as Record<string, unknown> | undefined)?.q ?? '');
  const found = catalog.filter((voice: Record<string, unknown>) =>
    String(voice.id).includes(q) || String(voice.displayName).includes(q));

  return {
    data: { data: onTheWire(found), total: found.length },
    response: { ok: true, status: 200 },
  };
}

function personVoice(options: Options | undefined): unknown {
  const voiceId = personVoices[userIdOf(options)];

  if (voiceId === undefined)
    return { error: { status: 'not_found' }, response: { ok: false, status: 404 } };

  return {
    data: { data: { userId: userIdOf(options), voiceId } },
    response: { ok: true, status: 200 },
  };
}

// What GET chat/viewer/users/{userId}/profile answers a caller who may not read the community.
const publicProfiles: Record<string, Record<string, unknown>> = {
  u1: {
    twitchUserId: 'u1',
    internalUserId: 'g1',
    username: 'ada',
    displayName: 'Ada',
    profileImageUrl: null,
    color: '#FF7F50',
    pronouns: 'she/her',
    badges: [{
      setId: 'subscriber',
      id: '3',
      info: '14',
    }],
    accountCreatedAt: '2015-06-01T00:00:00Z',
    followedAt: '2024-08-15T00:00:00Z',
    subTier: '1000',
    emotes: [
      {
        code: 'adaHi',
        provider: 'Twitch',
        urls: { 1: 'https://cdn/adaHi/1', 3: 'https://cdn/adaHi/3' },
        animated: false,
        zeroWidth: false,
        id: 'e1',
        type: 'subscriptions',
        tier: 1,
        staticUrls: { 1: 'https://cdn/adaHi/1', 3: 'https://cdn/adaHi/3' },
      },
    ],
    recentMessages: [{
      id: 'm1',
      message: 'public hello from ada',
      sentAt: '2026-10-01T10:00:00Z',
    }],
  },
};

let held: string[] = [];

// The emotes usable in the channel, as the chat emote list sends them.
const channelEmotes = [{
  id: 'hmmm',
  code: 'stoney90Hmmm',
  provider: 'Twitch',
  animated: false,
  zeroWidth: false,
  urls: { 1: 'https://cdn.example/hmmm/1x.png', 2: 'https://cdn.example/hmmm/2x.png' },
}];

type Options = { params?: Record<string, unknown>; body?: unknown };

function record(path: string, options: Options | undefined): void {
  calls.push({
    path,
    params: options?.params,
    body: options?.body,
  });
}

function userIdOf(options: Options | undefined): string {
  return String((options?.params?.path as Record<string, unknown> | undefined)?.userId);
}

function noteIdOf(options: Options | undefined): number {
  return Number((options?.params?.path as Record<string, unknown> | undefined)?.noteId);
}

function mapNotes(
  change: (note: Record<string, unknown>) => Record<string, unknown>,
): Record<string, Record<string, unknown>[]> {
  return Object.fromEntries(Object.entries(notes).map(([user, list]) => [user, list.map(change)]));
}

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: vi.fn<(path: string, options?: Options) => Promise<unknown>>(async (path, options) => {
      record(path, options);

      if (path.includes('/chat/viewer/users/')) {
        return {
          data: { data: onTheWire(publicProfiles[userIdOf(options)] ?? null) },
          response: { ok: true, status: 200 },
        };
      }

      if (path.endsWith('/profile'))
        return { data: { data: onTheWire(profiles[userIdOf(options)] ?? null) } };

      if (path.endsWith('/chat/emotes')) {
        return {
          data: { data: onTheWire(channelEmotes) },
          response: { ok: true, status: 200 },
        };
      }

      if (path.endsWith('/templates/helpers'))
        return { data: { data: [] }, response: { ok: true, status: 200 } };

      if (path.endsWith('/roles/effective/me'))
        return { data: { data: { heldActionKeys: [...held] } }, response: { ok: true } };

      if (path.endsWith('/auth/me'))
        return { data: { data: { username: signedInAs } }, response: { ok: true, status: 200 } };

      if (path.endsWith('/history/{userId}'))
        return { data: historyPage(options), response: { ok: true } };

      if (path.endsWith('/chat/users/{userId}/messages'))
        return chatPage(options);

      if (path.endsWith('/tts/users/{userId}/voice'))
        return personVoice(options);

      if (path.endsWith('/tts/voices'))
        return voicesPage(options);

      if (path.endsWith('/context')) {
        const standing = standings[userIdOf(options)];
        const context = {
          userId: userIdOf(options),
          banCount: 0,
          timeoutCount: 0,
          warnCount: 0,
          unbanCount: 0,
          recentActions: [],
          standings: standing
            ? [standing]
            : [],
        };

        return { data: { data: onTheWire(context) }, response: { ok: true } };
      }

      if (path.endsWith('/notes')) {
        return {
          data: { data: onTheWire(notes[userIdOf(options)] ?? []) },
          response: { ok: true },
        };
      }

      if (path.endsWith('/emotes')) {
        return {
          data: { data: onTheWire(emotes[userIdOf(options)] ?? []) },
          response: { ok: true },
        };
      }

      return { data: { data: { ...people[userIdOf(options)] } } };
    }),
    POST: vi.fn<(path: string, options?: Options) => Promise<unknown>>(async (path, options) => {
      record(path, options);

      if (path.endsWith('/vip')) {
        if (vipWrite) {
          await vipWrite;
        }

        if (vipWriteFails)
          return { error: { status: 'error' }, response: { ok: false } };

        people[userIdOf(options)]!.isVip = true;
      }

      if (path.endsWith('/standing')) {
        const id = userIdOf(options);
        const change = options?.body as {
          provider: string;
          standing: string;
          reason: string | null;
        };

        standings[id] = {
          userId: id,
          ...change,
          updatedAt: '2026-10-04T10:00:00Z',
        };

        noteStanding(id, `standing set to ${change.standing} (${change.provider})`);
      }

      return { response: { ok: true } };
    }),
    PUT: vi.fn<(path: string, options?: Options) => Promise<unknown>>(async (path, options) => {
      record(path, options);

      if (path.endsWith('/voice')) {
        const body = options?.body as { voiceId?: string } | undefined;

        personVoices[userIdOf(options)] = body?.voiceId;

        return { data: { data: null }, response: { ok: true, status: 200 } };
      }

      if (path.endsWith('/shoutout-overrides')) {
        const line = options?.body as { messageTemplate: string };

        profiles.g1 = {
          ...profiles.g1,
          overrides: { shoutoutMessageTemplate: line.messageTemplate, ttsVoice: null },
        };

        return { response: { ok: true, status: 204 } };
      }

      const id = noteIdOf(options);
      const change = options?.body as { content: string; pinned: boolean };

      notes = mapNotes((note: Record<string, unknown>) =>
        note.id === id
          ? { ...note, ...change }
          : note);

      return { data: { data: null }, response: { ok: true } };
    }),
    DELETE: vi.fn<(path: string, options?: Options) => Promise<unknown>>(async (path, options) => {
      record(path, options);

      if (path.endsWith('/shoutout-overrides/{targetTwitchUserId}')) {
        profiles.g1 = {
          ...profiles.g1,
          overrides: { shoutoutMessageTemplate: null, ttsVoice: null },
        };

        return { response: { ok: true, status: 204 } };
      }

      if (path.endsWith('/notes/{noteId}')) {
        const id = noteIdOf(options);

        notes = Object.fromEntries(Object.entries(notes).map(([user, list]) =>
          [user, list.filter((note: Record<string, unknown>) => note.id !== id)]));

        return { response: { ok: true } };
      }

      if (path.endsWith('/voice')) {
        personVoices[userIdOf(options)] = undefined;

        return { response: { ok: true, status: 200 } };
      }

      if (path.endsWith('/standing')) {
        const id = userIdOf(options);

        noteStanding(id, `standing cleared (twitch, was ${String(standings[id]?.standing)})`);
        standings[id] = undefined;

        return { response: { ok: true } };
      }

      people[userIdOf(options)]!.isVip = false;

      return { response: { ok: true } };
    }),
  },
}));

// The login of the channel being managed; a test sets it to make a person the channel's own account.
// The live channel list sends own channels without a Twitch id, so the login is what identifies them.
const channelLogin = ref('somebody');
// Who is signed in; a test sets it to make the caller a moderator, or the person themself.
let signedInAs = 'somebody';

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({
    channelId: ref('c1'),
    current: computed(() => ({ login: channelLogin.value, twitchId: null })),
  }),
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;
let router: Router;

// jsdom has no layout: the chat list needs a viewport and row heights to render any rows.
beforeEach(async () => {
  calls.length = 0;
  channelLogin.value = 'somebody';
  signedInAs = 'somebody';
  people = freshPeople();
  profiles = freshProfiles();
  notes = freshNotes();
  standings = {};
  chatPages = { g1: [[chatLine('c1', 'hello from ada', '2026-10-01T10:00:00Z')]] };
  personVoices = {};
  held = [...MODERATOR_KEYS];
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date('2026-10-04T10:00:00Z'));
  vipWrite = null;
  vipWriteFails = false;

  for (const observer of ['ResizeObserver', 'IntersectionObserver']) {
    vi.stubGlobal(
      observer,
      class {
        observe(): void {}
        unobserve(): void {}
        disconnect(): void {}
      },
    );
  }

  const sizeOf = (element: Element): number =>
    element.hasAttribute('data-virtual-scroll')
      ? 300
      : 40;

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

  pinia = createPinia();
  setActivePinia(pinia);

  router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/chat', component: { render: () => null } },
      {
        path: '/people',
        name: 'dashboard-people',
        component: { render: () => null },
      },
    ],
  });

  await router.push('/chat');
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

const Page = defineComponent({
  components: { ProfileDialog, UserName },
  template: `<div><UserName user-id="u1" name="Ada" /><UserName user-id="u2" name="Bo" /><ProfileDialog /></div>`,
});

async function openPage(): Promise<void> {
  mounted = mount(Page, {
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

  await flushPromises();
}

const name = (id: string): HTMLButtonElement =>
  document.body.querySelector(`[data-user="${id}"]`) as HTMLButtonElement;
const dialogs = (): NodeListOf<Element> => document.body.querySelectorAll('[role="dialog"]');
const button = (selector: string): HTMLButtonElement =>
  document.body.querySelector(selector) as HTMLButtonElement;

const settle = async (): Promise<void> => {
  await flushPromises();
  await new Promise((done) => setTimeout(done, 0));
  await flushPromises();
};

async function openUser(id: string): Promise<void> {
  name(id).focus();
  name(id).click();
  await settle();
}

// jsdom has no pointer capture, so a menu opens the keyboard way (Enter on the trigger).
function openMenu(trigger: HTMLElement): void {
  trigger.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
}

// The VIP choice is a button in the action row, beside Shout out and Warn.
async function vipItem(): Promise<HTMLElement> {
  await settle();

  return button('[data-profile-vip]');
}

async function openTab(label: string): Promise<void> {
  const tab = Array.from(document.body.querySelectorAll<HTMLElement>('[role="tab"]'))
    .find((element: HTMLElement) => element.textContent?.trim() === label);

  tab!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }));
  await settle();
}

describe('the profile dialog', () => {
  it('opens one dialog with the data of the clicked name', async () => {
    await openPage();
    expect(dialogs().length).toBe(0);

    await openUser('u1');

    expect(dialogs().length).toBe(1);
    const text = dialogs()[0]!.textContent ?? '';

    expect(text).toContain('Ada');
    expect(text).toContain('@ada');
    const detail = calls.find((c: Call) => c.path.endsWith('/community/{userId}'));

    expect(detail?.params).toEqual({
      path: {
        version: '1',
        channelId: 'c1',
        userId: 'u1',
      },
    });
  });

  it('swaps the user in the same dialog when another name opens', async () => {
    await openPage();
    await openUser('u1');

    useProfileDialogStore().open('u2', name('u2'));
    await settle();

    expect(dialogs().length).toBe(1);
    const text = dialogs()[0]!.textContent ?? '';

    expect(text).toContain('Bo');
    expect(text).not.toContain('@ada');
  });

  it('shows the chat lines on the Chat tab, with no tab that repeats the header counts', async () => {
    await openPage();
    await openUser('u1');

    const tabs: HTMLElement[] = Array.from(document.body.querySelectorAll('[role="tab"]'));

    expect(tabs.map((t: HTMLElement) => t.textContent?.trim())).toEqual([
      'Overview',
      'Chat',
      'Emotes',
      'Moderation',
      'Points',
      'Data',
      'Settings',
    ]);

    tabs[1]!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }));
    await settle();

    const active = dialogs()[0]!.querySelector('[role="tabpanel"][data-state="active"]');

    expect(active?.textContent).toContain('hello from ada');
  });

  // Audit 2026-10-15: on a phone the dialog scrolled past the tallest tab's empty space and pushed the actions out
  // of reach. Below md the dialog scrolls as one, the actions stay pinned below, and a hidden tab takes no room.
  // Owner 2026-10-15: the desktop dialog keeps its full size, so none of this applies from md up.
  it('on a phone scrolls header and tabs as one, keeps the actions outside it, and gives a hidden tab no room', async () => {
    await openPage();
    await openUser('u1');

    const dialog = dialogs()[0]!;
    const body = dialog.querySelector<HTMLElement>('[data-profile-body]')!;
    const panels: HTMLElement[] = Array.from(dialog.querySelectorAll('[role="tabpanel"]'));

    expect(body.classList).toContain('max-md:overflow-y-auto');
    expect(body.classList).not.toContain('overflow-y-auto');
    expect(body.contains(dialog.querySelector('[role="tablist"]'))).toBe(true);
    expect(body.contains(dialog.querySelector('[data-profile-ban]'))).toBe(false);
    expect(panels).toHaveLength(7);

    expect(panels.filter((panel) => panel.dataset.state === 'inactive').every((panel) =>
      panel.classList.contains('max-md:data-[state=inactive]:hidden') &&
      panel.classList.contains('data-[state=inactive]:invisible'))).toBe(true);
  });

  // The live API leaves a never-asked consent out of the answer; the tab once printed profile.yesNo.undefined.
  it('says in words that a person was never asked to confirm their age', async () => {
    await openPage();
    await openUser('u1');

    const data = Array.from(document.body.querySelectorAll<HTMLElement>('[role="tab"]'))
      .find((tab: HTMLElement) => tab.textContent?.trim() === 'Data');

    data!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }));
    await settle();

    const panel = dialogs()[0]!.querySelector('[role="tabpanel"][data-state="active"]');

    expect(panel?.textContent).toContain('Not asked yet');
    expect(panel?.textContent).not.toContain('profile.');
    expect(panel?.textContent).toContain('No active permits.');
  });

  it('says what each active permit grants, until when and why', async () => {
    const permit = {
      userId: 'g1',
      grantedByUserId: 'g9',
      revokedAt: null,
      createdAt: '2026-10-01T10:00:00Z',
    };

    profiles.g1!.permits = {
      ageConsentGranted: null,
      ageConsentConfirmedUtc: null,
      activePermits: [
        // The API sends the grant type and the role as numbers: Role is 0, LeadModerator is 1.
        {
          ...permit,
          id: 'p1',
          grantType: 0,
          grantedRole: 1,
          expiresAt: '2026-10-20T10:00:00Z',
          reason: 'Raid night',
        },
        {
          ...permit,
          id: 'p2',
          grantType: 1,
          grantedRole: null,
          capabilityActionKey: 'moderation:timeout',
        },
      ],
    };

    await openPage();
    await openUser('u1');
    await openTab('Data');

    const permits = Array.from(document.body.querySelectorAll('[data-profile-permits] li'))
      .map((line: Element) => line.textContent?.trim());

    expect(permits).toEqual([
      'Lead moderator role, until Oct 20, 2026Raid night',
      'May use moderation:timeout',
    ]);
  });

  it('lists the ban history of a banned person on the Moderation tab', async () => {
    await openPage();
    await openUser('u2');
    await openTab('Moderation');

    const active = dialogs()[0]!.querySelector('[role="tabpanel"][data-state="active"]');

    expect(active?.textContent).toContain('spam links');
    expect(active?.textContent).toContain('Stoney_Eagle');

    // A lifted ban says when it was lifted; the ban still in force says nothing it does not know.
    const lifted = Array.from(active!.querySelectorAll('[data-profile-ban-lifted]'))
      .map((line: Element) => line.textContent?.trim());

    expect(lifted).toEqual(['Lifted on Aug 3, 2026']);
  });

  it('asks before it bans and calls the ban endpoint only after the confirm', async () => {
    await openPage();
    await openUser('u1');

    button('[data-profile-ban]').click();
    await settle();
    expect(document.body.querySelector('[role="alertdialog"]')).not.toBeNull();
    expect(calls.filter((c: Call) => c.path.endsWith('/ban'))).toEqual([]);

    button('[data-confirm-action]').click();
    await settle();

    const bans = calls.filter((c: Call) => c.path.endsWith('/ban'));

    expect(bans.length).toBe(1);

    expect(bans[0]!.params).toEqual({
      path: {
        version: '1',
        channelId: 'c1',
        userId: 'u1',
      },
    });

    expect(typeof (bans[0]!.body as { reason: string }).reason).toBe('string');
    expect(document.body.querySelector('[role="alertdialog"]')).toBeNull();
  });

  it('does not ban when the confirm is canceled', async () => {
    await openPage();
    await openUser('u1');

    button('[data-profile-ban]').click();
    await settle();
    const cancel = Array.from(document.body.querySelectorAll('[role="alertdialog"] button')).find(
      (b: Element) => b.textContent?.trim() === 'Cancel',
    ) as HTMLButtonElement;

    cancel.click();
    await settle();

    expect(calls.filter((c: Call) => c.path.endsWith('/ban'))).toEqual([]);
  });

  it('flips the VIP choice at once and keeps it when the save works', async () => {
    await openPage();
    await openUser('u1');
    expect((await vipItem()).textContent).toContain('Make VIP');

    let release: () => void = () => {};

    vipWrite = new Promise<void>((done) => {
      release = done;
    });

    (await vipItem()).click();
    await flushPromises();
    expect((await vipItem()).textContent).toContain('Remove VIP');

    release();
    await settle();
    expect((await vipItem()).textContent).toContain('Remove VIP');
    expect(calls.some((c: Call) => c.path.endsWith('/vip'))).toBe(true);
  });

  it('rolls the VIP choice back and tells the error inbox when the save fails', async () => {
    await openPage();
    await openUser('u1');
    vipWriteFails = true;

    (await vipItem()).click();
    await settle();

    expect((await vipItem()).textContent).toContain('Make VIP');
    expect(useSaveErrorsStore().items.length).toBe(1);
  });

  it('takes the VIP state from the person Twitch was asked about, not from a VIP list', async () => {
    people.u1!.isVip = true;
    await openPage();
    await openUser('u1');

    expect((await vipItem()).textContent).toContain('Remove VIP');
    const reads = [
      '/community/{userId}',
      '/profile',
      '/emotes',
      '/notes',
      '/roles/effective/me',
      '/auth/me',
      '/history/{userId}',
      '/context',
      '/chat/users/{userId}/messages',
    ];
    const writes = calls.filter((c: Call) =>
      !reads.some((end: string) =>
        c.path.endsWith(end)));

    expect(writes).toEqual([]);
  });

  it('says the VIP state is unknown and offers no VIP change when Twitch could not be asked', async () => {
    await openPage();
    await openUser('u2');

    const item = await vipItem();

    expect((item as HTMLButtonElement).disabled).toBe(true);
    expect(item.textContent).toContain('VIP status unknown');
  });

  it('times a person out for the length chosen from the Timeout menu', async () => {
    await openPage();
    await openUser('u1');

    openMenu(button('[data-profile-timeout]'));
    await settle();
    button('[data-profile-timeout-length="600"]').click();
    await settle();

    const timeouts = calls.filter((c: Call) => c.path.endsWith('/moderation/actions'));

    expect(timeouts.map((c: Call) => c.body)).toEqual([
      {
        action: 'timeout',
        targetUserId: 'u1',
        durationSeconds: 600,
      },
    ]);
  });

  describe('who may be timed out or banned', () => {
    const sanctionButtons = (): HTMLElement[] =>
      ['[data-profile-timeout]', '[data-profile-ban]'].flatMap((selector: string) =>
        [...document.body.querySelectorAll<HTMLElement>(selector)]);

    it('shows both to a moderator on a viewer', async () => {
      signedInAs = 'helper';
      const ada = profiles.g1 as { identity: { managementRole: string | null } };

      ada.identity.managementRole = null;
      await openPage();
      await openUser('u1');
      expect(sanctionButtons()).toHaveLength(2);
    });

    it('shows neither on yourself', async () => {
      signedInAs = 'ada';
      await openPage();
      await openUser('u1');
      expect(sanctionButtons()).toHaveLength(0);
    });

    it('shows neither on the broadcaster', async () => {
      signedInAs = 'helper';
      channelLogin.value = 'ada';
      await openPage();
      await openUser('u1');
      expect(sanctionButtons()).toHaveLength(0);
    });

    it('shows neither to a moderator on another moderator', async () => {
      signedInAs = 'helper';
      people.u1!.isModerator = true;
      await openPage();
      await openUser('u1');
      expect(sanctionButtons()).toHaveLength(0);
    });

    it('shows both to the broadcaster on a moderator', async () => {
      people.u1!.isModerator = true;
      await openPage();
      await openUser('u1');
      expect(sanctionButtons()).toHaveLength(2);
    });
  });

  it('shows Shout out, VIP and Warn as buttons, not hidden in a menu', async () => {
    await openPage();
    await openUser('u1');

    expect(document.body.querySelector('[data-profile-more]')).toBeNull();

    expect(['shoutout', 'vip', 'warn'].map((name: string) =>
      document.body.querySelector(`[data-profile-${name}]`)?.tagName)).toEqual(['BUTTON', 'BUTTON', 'BUTTON']);
  });

  it('shouts a person out from its button', async () => {
    await openPage();
    await openUser('u1');

    button('[data-profile-shoutout]').click();
    await settle();

    const shoutouts = calls.filter((c: Call) => c.path.endsWith('/moderation/shoutout'));

    expect(shoutouts.map((c: Call) => c.body)).toEqual([{ targetTwitchUserId: 'u1' }]);
  });

  it('warns the person with the reason the moderator typed', async () => {
    await openPage();
    await openUser('u1');

    button('[data-profile-warn]').click();
    await settle();

    const reason = document.body.querySelector<HTMLTextAreaElement>('[data-profile-warn-reason]')!;

    // An empty reason is refused here, as Twitch would refuse it.
    button('[data-form-action]').click();
    await settle();

    expect(calls.filter((c: Call) => c.path.endsWith('/moderation/warn'))).toHaveLength(0);
    expect(document.body.textContent).toContain('Twitch needs a reason');

    reason.value = '  Please stop the caps  ';
    reason.dispatchEvent(new Event('input'));
    await settle();
    button('[data-form-action]').click();
    await settle();

    const warns = calls.filter((c: Call) => c.path.endsWith('/moderation/warn'));

    expect(warns.map((c: Call) => c.body)).toEqual([
      { targetUserId: 'u1', reason: 'Please stop the caps' },
    ]);

    expect(document.body.querySelector('[data-profile-warn-reason]')).toBeNull();
  });

  it('locks Warn for a caller without the warn right', async () => {
    held = MODERATOR_KEYS.filter((key: string) => key !== 'moderation:warn');
    await openPage();
    await openUser('u1');

    const warn = document.body.querySelector<HTMLButtonElement>('[data-profile-warn]')!;

    expect(warn.disabled).toBe(true);
    expect(warn.getAttribute('title')).toBe(i18n.global.t('common.readOnly'));
  });

  // Owner 2026-10-15: "ban timeout and warn are mod actions so why is one of them a link button?"
  it('keeps Warn with Timeout and Ban, in the same look as Timeout', async () => {
    await openPage();
    await openUser('u1');

    const warn = document.body.querySelector<HTMLElement>('[data-profile-warn]')!;
    const timeout = document.body.querySelector<HTMLElement>('[data-profile-timeout]')!;

    expect(warn.parentElement).toBe(timeout.parentElement);
    expect(warn.dataset.variant).toBe(timeout.dataset.variant);
  });

  it('offers Unban instead of Ban and Timeout for a banned person, and lifts the ban', async () => {
    await openPage();
    await openUser('u2');

    expect(document.body.querySelector('[data-profile-warn]')).toBeNull();
    expect(document.body.querySelector('[data-profile-ban]')).toBeNull();
    expect(document.body.querySelector('[data-profile-timeout]')).toBeNull();

    button('[data-profile-unban]').click();
    await settle();

    const unbans = calls.filter((c: Call) => c.path.endsWith('/moderation/actions/unban'));

    expect(unbans.map((c: Call) => c.body)).toEqual([
      { targetTwitchUserId: 'u2', scope: 'this_channel' },
    ]);
  });

  it('lists the moderator notes on the Moderation tab and adds a new one', async () => {
    await openPage();
    await openUser('u1');
    await openTab('Moderation');

    const panel = (): Element =>
      dialogs()[0]!.querySelector('[role="tabpanel"][data-state="active"]')!;

    expect(panel().textContent).toContain('Helps new chatters find the rules.');
    expect(panel().textContent).toContain('Pinned · Stoney_Eagle ·');

    const box = panel().querySelector<HTMLTextAreaElement>('[data-profile-note-input]')!;

    box.value = 'Asked about the giveaway rules.';
    box.dispatchEvent(new Event('input', { bubbles: true }));
    await settle();
    button('[data-profile-note-add]').click();
    await settle();

    const added = calls.filter((c: Call) => c.path.endsWith('/notes') && c.body !== undefined);

    expect(added.map((c: Call) => c.body)).toEqual([
      { content: 'Asked about the giveaway rules.', pinned: false },
    ]);

    expect(box.value).toBe('');
  });

  it('locks Ban and Timeout, and offers no write, when the caller may not ban here', async () => {
    held = held.filter((key: string) => key !== 'moderation:ban');
    await openPage();
    await openUser('u1');

    expect(button('[data-profile-ban]').disabled).toBe(true);
    expect(button('[data-profile-timeout]').disabled).toBe(true);

    expect(button('[data-profile-ban]').title).toBe(
      'You can view this, but your role cannot change it.',
    );

    expect(((await vipItem()) as HTMLButtonElement).disabled).toBe(false);
  });

  it('edits a note and its pin from the note menu, keeping the rest of the note', async () => {
    await openPage();
    await openUser('u1');
    await openTab('Moderation');

    openMenu(button('[data-profile-note-menu]'));
    await settle();
    button('[data-profile-note-edit]').click();
    await settle();

    const text = document.body.querySelector<HTMLTextAreaElement>('[data-profile-note-edit-text]')!;

    expect(text.value).toBe('Helps new chatters find the rules.');

    text.value = '  Helps new chatters find the rules and the FAQ.  ';
    text.dispatchEvent(new Event('input'));
    button('[data-profile-note-edit-pin]').click();
    await settle();
    button('[data-form-action]').click();
    await settle();

    const edits = calls.filter((c: Call) => c.path.endsWith('/notes/{noteId}') && c.body !== undefined);

    expect(edits.map((c: Call) => [noteIdOf(c), c.body])).toEqual([
      [7, { content: 'Helps new chatters find the rules and the FAQ.', pinned: false }],
    ]);

    expect(document.body.querySelector('[data-profile-note-edit-text]')).toBeNull();
    expect(dialogs()[0]!.textContent).toContain('Helps new chatters find the rules and the FAQ.');
    expect(dialogs()[0]!.textContent).not.toContain('Pinned ·');
  });

  it('deletes a note only after the moderator confirms it', async () => {
    await openPage();
    await openUser('u1');
    await openTab('Moderation');

    openMenu(button('[data-profile-note-menu]'));
    await settle();
    button('[data-profile-note-delete]').click();
    await settle();

    const deletes = (): Call[] =>
      calls.filter((c: Call) => c.path.endsWith('/notes/{noteId}') && c.body === undefined);

    expect(deletes()).toHaveLength(0);

    button('[data-confirm-action]').click();
    await settle();

    expect(deletes().map((c: Call) => noteIdOf(c))).toEqual([7]);
    expect(dialogs()[0]!.textContent).not.toContain('Helps new chatters find the rules.');
    expect(dialogs()[0]!.textContent).toContain('No notes on this person yet.');
  });

  it('sets a bot standing with its reason, shows its effect at once and reloads the note the server wrote', async () => {
    await openPage();
    await openUser('u1');
    await openTab('Moderation');

    const panel = (): Element =>
      dialogs()[0]!.querySelector('[data-slot="profile-standing"]')!;

    expect(panel().textContent).toContain('The bot treats them like everyone else.');

    button('[data-profile-standing-change]').click();
    await settle();
    button('[data-profile-standing-choice="shadowbanned"]').click();
    await settle();

    const reason = document.body.querySelector<HTMLTextAreaElement>('[data-profile-standing-reason]')!;

    reason.value = '  Raid bot  ';
    reason.dispatchEvent(new Event('input'));
    await settle();
    button('[data-form-action]').click();
    await settle();

    const writes = calls.filter((c: Call) => c.path.endsWith('/standing'));

    expect(writes.map((c: Call) => [userIdOf(c), c.body])).toEqual([
      ['u1', {
        provider: 'twitch',
        standing: 'shadowbanned',
        reason: 'Raid bot',
      }],
    ]);

    expect(document.body.querySelector('[data-profile-standing-dialog]')).toBeNull();
    expect(panel().textContent).toContain('Shadowbanned');
    expect(panel().textContent).toContain('Raid bot');
    expect(panel().textContent).toContain('their messages and events stay off your overlays');
    expect(dialogs()[0]!.textContent).toContain('standing set to shadowbanned (twitch)');
  });

  it('puts a person back to normal by clearing their Twitch standing', async () => {
    standings = {
      u1: {
        userId: 'u1',
        provider: 'twitch',
        standing: 'muted',
        reason: 'Spam',
        updatedAt: '2026-10-01T10:00:00Z',
      },
    };

    await openPage();
    await openUser('u1');
    await openTab('Moderation');

    const panel = (): Element =>
      dialogs()[0]!.querySelector('[data-slot="profile-standing"]')!;

    expect(panel().textContent).toContain('Muted');
    expect(panel().textContent).toContain('the bot ignores them');

    button('[data-profile-standing-change]').click();
    await settle();

    expect(button('[data-profile-standing-choice="muted"]').getAttribute('data-state')).toBe('checked');

    button('[data-profile-standing-choice="normal"]').click();
    await settle();

    expect(document.body.querySelector('[data-profile-standing-reason]')).toBeNull();

    button('[data-form-action]').click();
    await settle();

    const clears = calls.filter((c: Call) =>
      c.path.endsWith('/standing') && c.body === undefined);

    expect(clears.map((c: Call) => [userIdOf(c), c.params?.query])).toEqual([
      ['u1', { provider: 'twitch' }],
    ]);

    expect(panel().textContent).toContain('The bot treats them like everyone else.');
    expect(dialogs()[0]!.textContent).toContain('standing cleared (twitch, was muted)');
  });

  it('locks the standing change for a caller without the standing right', async () => {
    held = held.filter((key: string) => key !== 'moderation:suspicioususer:write');
    await openPage();
    await openUser('u1');
    await openTab('Moderation');

    const change = button('[data-profile-standing-change]');

    expect(change.disabled).toBe(true);
    expect(change.title).toBe('Your role cannot change the bot standing.');
  });

  it('locks the note box when the caller may read notes but not write them', async () => {
    held = held.filter((key: string) => key !== 'moderation:note:write');
    await openPage();
    await openUser('u1');
    await openTab('Moderation');

    const scope = dialogs()[0]!.querySelector<HTMLFieldSetElement>('[data-slot="read-only-scope"]')!;

    expect(scope.disabled).toBe(true);
    expect(dialogs()[0]!.textContent).toContain('Helps new chatters find the rules.');
  });

  it('lists what moderation did to the person, by whom and why, and shows older rows on request', async () => {
    await openPage();
    await openUser('u1');
    await openTab('Moderation');

    const rows = (): string[] =>
      Array.from(dialogs()[0]!.querySelectorAll('[data-profile-history]')).map((row: Element) =>
        row.textContent ?? '');

    expect(rows()).toHaveLength(2);
    expect(rows()[0]).toContain('Timed out for 10 min');
    expect(rows()[0]).toContain('Spam links');
    expect(rows()[0]).toContain('Stoney_Eagle ·');
    expect(rows()[1]).toContain('Caught by a chat filter');
    expect(rows()[1]).toContain('Automatic ·');

    const asked = calls.filter((c: Call) => c.path.endsWith('/history/{userId}'));

    expect(asked.map((c: Call) => userIdOf(c))).toEqual(['g1']);

    button('[data-profile-history-more]').click();
    await settle();

    expect(rows()).toHaveLength(3);
    expect(rows()[2]).toContain('Warned');
    expect(rows()[2]).toContain('Caps');
    expect(document.body.querySelector('[data-profile-history-more]')).toBeNull();
  });

  it('does not ask for the moderation history when the caller may not read it', async () => {
    held = held.filter((key: string) => key !== 'moderation:history:read');
    await openPage();
    await openUser('u1');
    await openTab('Moderation');

    expect(calls.some((c: Call) => c.path.endsWith('/history/{userId}'))).toBe(false);
    expect(dialogs()[0]!.textContent).toContain('Your role cannot see the moderation history.');
  });

  it('offers no note save while the note is empty', async () => {
    await openPage();
    await openUser('u1');
    await openTab('Moderation');

    expect(button('[data-profile-note-add]').disabled).toBe(true);
  });

  it('closes on Esc and returns the focus to the name that opened it', async () => {
    await openPage();
    await openUser('u1');
    expect(dialogs().length).toBe(1);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();

    expect(dialogs().length).toBe(0);
    expect(useProfileDialogStore().userId).toBeNull();
    expect(document.activeElement).toBe(name('u1'));
  });

  // Mockup profile.js pf-bar: the dialog links to the person's own page, on the tab open in the dialog.
  it('opens the full page of the person on the tab open here, and closes itself', async () => {
    await openPage();
    await openUser('u1');

    const link = (): HTMLAnchorElement =>
      document.body.querySelector<HTMLAnchorElement>('[data-profile-full-page]')!;

    expect(link().getAttribute('href')).toBe('/people?u=ada');

    await openTab('Moderation');
    expect(link().getAttribute('href')).toBe('/people?u=ada&tab=moderation');

    link().click();
    await settle();

    expect(router.currentRoute.value.fullPath).toBe('/people?u=ada&tab=moderation');
    expect(dialogs().length).toBe(0);
  });

  it('opens again on Overview, whatever tab it closed on', async () => {
    await openPage();
    await openUser('u1');
    await openTab('Moderation');

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();
    await openUser('u1');

    const active = dialogs()[0]!.querySelector('[role="tab"][data-state="active"]');

    expect(active?.textContent?.trim()).toBe('Overview');
    expect(button('[data-profile-full-page]').getAttribute('href')).toBe('/people?u=ada');
  });
});

const header = (): HTMLElement => document.body.querySelector('[data-slot="profile-header"]')!;
const overview = (): HTMLElement => document.body.querySelector('[data-slot="profile-overview"]')!;

describe('the profile header', () => {
  it('shows the pronouns, the role badges and when the person was last seen', async () => {
    people.u1!.isVip = true;
    await openPage();
    await openUser('u1');

    const text = header().textContent ?? '';

    expect(text).toContain('Ada');
    expect(text).toContain('she/her');
    expect(text).toContain('Moderator');
    expect(text).toContain('VIP');
    expect(text).toContain('Subscriber');

    expect(header().querySelector('[data-profile-age="lastSeen"] dd')?.textContent?.trim())
      .toBe('3 days ago');
  });

  // Mockup profile.css .who-badges: a role is a quiet muted badge with its icon, never an accent tint.
  it('shows each role as a muted badge with its icon', async () => {
    people.u1!.isVip = true;
    await openPage();
    await openUser('u1');

    const badges = Array.from(header().querySelectorAll('[data-profile-role]'));

    expect(badges.map((badge: Element) => badge.textContent?.trim())).toEqual([
      'Moderator',
      'VIP',
      'Subscriber',
    ]);

    for (const badge of badges) {
      expect(badge.querySelector('svg')).not.toBeNull();
      expect(badge.classList).toContain('bg-muted');
      expect(badge.classList).not.toContain('bg-secondary');
    }
  });

  it('names the channel\'s own account the broadcaster and offers no Ban or VIP on it', async () => {
    channelLogin.value = 'Ada';
    await openPage();
    await openUser('u1');

    const roles = Array.from(header().querySelectorAll('[data-profile-role]')).map((badge: Element) =>
      badge.textContent?.trim());

    expect(roles[0]).toBe('Broadcaster');
    expect(document.body.querySelector('[data-profile-ban]')).toBeNull();
    expect(document.body.querySelector('[data-profile-vip]')).toBeNull();
  });

  it('never calls someone else the broadcaster because of a stored role', async () => {
    (profiles.g1 as { identity: { managementRole: string } }).identity.managementRole = 'Broadcaster';
    await openPage();
    await openUser('u1');

    const roles = Array.from(header().querySelectorAll('[data-profile-role]')).map((badge: Element) =>
      badge.textContent?.trim());

    expect(roles).not.toContain('Broadcaster');
    expect(document.body.querySelector('[data-profile-ban]')).not.toBeNull();
  });

  it('shows the Twitch channel standing with the Twitch logo', async () => {
    await openPage();
    await openUser('u1');

    const standing = header().querySelector('[data-profile-standing]');

    expect(standing?.textContent?.trim()).toBe('Affiliate');
    expect(standing?.querySelector('[data-profile-platform]')).not.toBeNull();
  });

  it('shows no Regular mark for a Twitch default account', async () => {
    (profiles.g1 as { identity: { broadcasterType: string } }).identity.broadcasterType = '';
    await openPage();
    await openUser('u1');

    expect(header().querySelector('[data-profile-standing]')).toBeNull();
    expect(header().querySelector('[data-profile-regular]')).toBeNull();
  });

  it('shows the Regular mark when the bot trust tier is Regular', async () => {
    (profiles.g1 as { identity: { trustTier: string } }).identity.trustTier = 'Regular';
    await openPage();
    await openUser('u1');

    expect(header().querySelector('[data-profile-regular]')?.textContent?.trim()).toBe('Regular');
  });

  it('shows the trust tier and the trust score in the behavior block', async () => {
    (profiles.g1 as { identity: { trustTier: string } }).identity.trustTier = 'Known';
    (profiles.g1 as { trust: unknown }).trust = { trustScore: 62.4, heatScore: 3 };
    await openPage();
    await openUser('u1');

    const stat = (key: string): string | undefined =>
      header().querySelector(`[data-profile-behavior-stat="${key}"] b`)?.textContent?.trim();

    expect(stat('tier')).toBe('Known');
    expect(stat('trust')).toBe('62');
    expect(header().querySelector('[data-profile-regular]')).toBeNull();
  });

  // Owner 2026-10-14: the sword for mods and the diamond for VIPs in Twitch's colors, the subscriber in Twitch
  // purple, and the account status first in the row.
  it('leads with the account status and colors each role as Twitch does', async () => {
    people.u1!.isVip = true;
    await openPage();
    await openUser('u1');

    const row = header().querySelector('[data-profile-standing]')!.parentElement!;
    const badge = (name: string): Element =>
      [...row.querySelectorAll('[data-profile-role]')].find((b: Element) =>
        b.textContent?.trim() === name)!;

    expect(row.firstElementChild?.hasAttribute('data-profile-standing')).toBe(true);

    expect(badge('Moderator').classList).toContain('bg-muted');

    expect(badge('Moderator').querySelector('[data-profile-platform]')?.getAttribute('fill'))
      .toBe('#9146FF');

    expect(badge('VIP').querySelector('svg')?.classList).toContain('text-role-vip');

    expect(badge('Subscriber').querySelector('[data-profile-platform]')?.getAttribute('fill'))
      .toBe('#9146FF');
  });

  // Mockup profile.js .stats: the number on top and its label under it, so a label that wraps never pushes
  // its number out of line with the others.
  // Owner 2026-10-14 "unify the look": every fact on the card is a label over its value.
  it('shows a stat for each number the API returns, its label above the number', async () => {
    await openPage();
    await openUser('u1');

    const stats = Array.from(header().querySelectorAll('[data-profile-stat]')).map(
      (tile: Element) => Array.from(tile.children).map((part: Element) => part.textContent).join(' '),
    );

    expect(stats).toEqual([
      'Chat lines 1,234',
      'Hours watched 12.5',
      'Commands used 7',
      'Rewards redeemed 9',
      'Watch streak 4',
    ]);
  });

  it('leaves out what the server does not know instead of inventing it', async () => {
    await openPage();
    await openUser('u2');

    const text = header().textContent ?? '';

    expect(text).not.toContain('Rewards redeemed');
    expect(text).not.toContain('Watch streak');
    expect(text).not.toContain('Subscriber');
    expect(text).not.toContain('VIP');
    expect(text).toContain('Chat lines');
  });
});

describe('the Overview tab', () => {
  it('lists the Relationship and Identity facts from the profile', async () => {
    await openPage();
    await openUser('u1');

    const text = overview().textContent ?? '';

    expect(text).toContain('Identity');
    expect(text).toContain('Relationship');
    expect(text).toContain('90210');
    // memberSinceUtc is when the channel role was given (ChannelMembership.GrantedAt), not the account date.
    expect(text).toContain('Role since');
    expect(text).toContain('Mar 4, 2020');
    expect(text).toContain('Tier 1');
  });

  it('repeats nothing the header already shows', async () => {
    await openPage();
    await openUser('u1');

    const labels = Array.from(overview().querySelectorAll('dt')).map((dt: Element) => dt.textContent);

    for (const label of ['Name', 'Pronouns', 'Account created', 'On Twitch', 'Following', 'Roles', 'First seen', 'Last seen']) {
      expect(labels).not.toContain(label);
    }

    expect(overview().textContent).not.toContain('she/her');
    expect(overview().textContent).not.toContain('Twitch affiliate');
  });

  it('shows the About text first, as the person wrote it, and not as a list row', async () => {
    await openPage();
    await openUser('u1');

    const about = overview().querySelector('[data-profile-about]');
    const labels = Array.from(overview().querySelectorAll('dt')).map((dt: Element) => dt.textContent);

    expect(about?.textContent).toBe('Writes compilers for fun.');
    expect(overview().firstElementChild).toBe(about);
    expect(labels).not.toContain('About');
  });

  it('shows the nickname, color, time zone, linked accounts and bits', async () => {
    await openPage();
    await openUser('u1');

    const text = overview().textContent ?? '';
    const swatch = overview().querySelector<HTMLElement>('[data-profile-color]');

    expect(text).toContain('Countess');
    expect(text).toContain('Europe/London');
    expect(text).toContain('Kick: AdaK, linked May 6, 2026');
    expect(text).toContain('Lurking');
    expect(text).toContain('Bits cheered');
    expect(text).toContain('500');
    expect(swatch?.style.backgroundColor).toBe('rgb(255, 127, 80)');
  });

  it('shows how many times the person raided this channel and how many times it raided them', async () => {
    await openPage();
    await openUser('u1');

    const rows = Array.from(overview().querySelectorAll('dt')).map((dt: Element) => [
      dt.textContent,
      dt.nextElementSibling?.textContent,
    ]);

    expect(rows).toContainEqual(['Raided this channel', '3']);
    expect(rows).toContainEqual(['This channel raided them', '1']);
  });

  it('says at the top how long the person has been around: account, follow and first sighting', async () => {
    await openPage();
    await openUser('u1');

    const ages = [...document.body.querySelectorAll('[data-profile-age]')].map((age: Element) =>
      [age.querySelector('dt')?.textContent?.trim(), age.querySelector('dd')?.textContent?.trim()]);

    expect(ages).toEqual([
      ['Last seen', '3 days ago'],
      ['Twitch account made', '11 years ago'],
      ['Followed', '2 years ago'],
      ['First seen here', '9 months ago'],
    ]);
  });

  it('omits a fact the server does not return', async () => {
    await openPage();
    await openUser('u2');

    const labels = Array.from(overview().querySelectorAll('dt')).map(
      (dt: Element) => dt.textContent,
    );

    // Facts the server does not return, an empty Twitch status, no color and not lurking are not worth a row.
    for (const label of ['Role since', 'Twitch ID', 'Subscription', 'Nickname', 'Time zone', 'Chat color', 'Lurking', 'Bits cheered', 'Raided this channel', 'This channel raided them']) {
      expect(labels).not.toContain(label);
    }

    expect(overview().querySelector('[data-profile-about]')).toBeNull();
  });

  it('keeps the chat lines in the Chat tab only, never in the Overview', async () => {
    chatPages.g1 = [[chatLine('c1', 'line one', '2026-10-01T10:00:00Z')]];

    await openPage();
    await openUser('u1');

    expect(overview().textContent).not.toContain('line one');
    expect(document.body.querySelector('[data-profile-chat-lines]')!.textContent).toContain('line one');
  });

  it('pages through the whole chat grouped by day, appending older lines until none are left', async () => {
    chatPages.g1 = [
      [
        chatLine('c3', 'newest', '2026-10-01T10:05:00Z'),
        chatLine('c2', 'same day', '2026-10-01T10:00:00Z'),
      ],
      [
        chatLine('c1', 'older same day', '2026-10-01T09:00:00Z'),
        chatLine('c0', 'first ever', '2026-09-15T09:00:00Z'),
      ],
    ];

    await openPage();
    await openUser('u1');

    const days = (): string[] =>
      Array.from(document.body.querySelectorAll('[data-profile-chat-day]')).map((day: Element) =>
        day.textContent ?? '');

    expect(days()).toHaveLength(1);
    expect(days()[0]).toContain('newest');
    expect(days()[0]).toContain('same day');
    expect(days()[0]).not.toContain('older same day');

    button('[data-profile-chat-more]').click();
    await settle();

    // The older line of the same day joins its group; the earlier day gets its own heading.
    expect(days()).toHaveLength(2);
    expect(days()[0]).toContain('older same day');
    expect(days()[1]).toContain('first ever');
    expect(document.body.querySelectorAll('[data-profile-chat-day-heading]')).toHaveLength(2);
    expect(document.body.querySelector('[data-profile-chat-more]')).toBeNull();

    const asked = calls.filter((c: Call) => c.path.endsWith('/chat/users/{userId}/messages'));

    expect(asked.map((c: Call) => userIdOf(c))).toEqual(['g1', 'g1']);
  });

  it('shows a known emote code in a chat line as the emote, and keeps other words as text', async () => {
    chatPages.g1 = [[chatLine('c1', 'nice raid stoney90Hmmm stoney90Unknown', '2026-10-01T10:00:00Z')]];

    await openPage();
    await openUser('u1');

    const scope = dialogs()[0]!.querySelector('[data-profile-chat-lines]')!;
    const emote = scope.querySelector<HTMLImageElement>('img[data-emote="stoney90Hmmm"]');

    expect(emote?.src).toBe('https://cdn.example/hmmm/1x.png');
    expect(emote?.alt).toBe('stoney90Hmmm');
    expect(scope.textContent).toContain('nice raid');
    expect(scope.textContent).toContain('stoney90Unknown');
  });
});

describe('the TTS voice panel', () => {
  const VOICE_KEYS = ['tts:voice:read', 'tts:uservoice:write'];
  const panel = (): Element => document.body.querySelector('[data-slot="profile-voice"]')!;
  const voiceWrites = (): Call[] =>
    calls.filter((c: Call) => c.path.endsWith('/tts/users/{userId}/voice') && c.body !== undefined);

  beforeEach(() => {
    held = [...merge(held, VOICE_KEYS)];
  });

  it('sits in the Settings tab, not in the Overview', async () => {
    await openPage();
    await openUser('u1');

    const tabPanel = panel().closest('[role="tabpanel"]')!;
    const settingsTab = Array.from(document.body.querySelectorAll('[role="tab"]')).find(
      (t: Element) => t.textContent?.trim() === 'Settings',
    );

    expect(settingsTab?.getAttribute('aria-controls')).toBe(tabPanel.id);
    expect(overview().closest('[role="tabpanel"]')).not.toBe(tabPanel);
  });

  it('says a person without their own voice uses the channel voice', async () => {
    await openPage();
    await openUser('u1');

    expect(panel().querySelector('[data-profile-voice-name]')!.textContent).toBe('Channel voice');
  });

  it('names the person\'s own voice from the catalog', async () => {
    personVoices = { u1: 'en-US-AvaNeural' };
    await openPage();
    await openUser('u1');

    expect(panel().querySelector('[data-profile-voice-name]')!.textContent).toBe('Ava');
  });

  it('saves the picked voice for the person and shows it at once', async () => {
    await openPage();
    await openUser('u1');

    button('[data-profile-voice-change]').click();
    await settle();
    button('[data-profile-voice-option="en-US-EmmaNeural"]').click();
    await settle();
    button('[data-form-action]').click();
    await settle();

    expect(voiceWrites().map((c: Call) => [userIdOf(c), c.body])).toEqual([
      ['u1', { voiceId: 'en-US-EmmaNeural' }],
    ]);

    expect(personVoices.u1).toBe('en-US-EmmaNeural');
    expect(panel().querySelector('[data-profile-voice-name]')!.textContent).toBe('Emma');
  });

  it('puts the person back on the channel voice by clearing their own', async () => {
    personVoices = { u1: 'en-US-AvaNeural' };
    await openPage();
    await openUser('u1');

    button('[data-profile-voice-change]').click();
    await settle();

    expect(button('[data-profile-voice-option="en-US-AvaNeural"]').getAttribute('aria-checked')).toBe('true');

    button('[data-profile-voice-option="channel"]').click();
    await settle();
    button('[data-form-action]').click();
    await settle();

    expect(voiceWrites()).toEqual([]);
    expect(personVoices.u1).toBeUndefined();
    expect(panel().querySelector('[data-profile-voice-name]')!.textContent).toBe('Channel voice');
  });

  it('locks the voice change for a caller without the voice right', async () => {
    held = held.filter((key: string) => key !== 'tts:uservoice:write');
    await openPage();
    await openUser('u1');

    const change = button('[data-profile-voice-change]');

    expect(change.disabled).toBe(true);
    expect(change.title).toBe('Your role cannot change a person\'s TTS voice.');
  });

  it('does not ask for the voice when the caller may not see the voices', async () => {
    held = held.filter((key: string) => !VOICE_KEYS.includes(key));
    await openPage();
    await openUser('u1');

    expect(calls.some((c: Call) => c.path.includes('/tts/'))).toBe(false);
    expect(panel().textContent).toContain('Your role cannot see the TTS voices.');
  });
});

describe('the shoutout line', () => {
  const OWN_LINE = '[data-profile-shoutout-text]';

  function shoutoutWrites(method: 'PUT' | 'DELETE'): Call[] {
    return calls.filter((c: Call) =>
      c.path.includes('/shoutout-overrides') &&
      (method === 'PUT') === (c.body !== undefined));
  }

  function targetOf(call: Call): string {
    const path = (call.params ?? {}).path as Record<string, unknown>;

    return String(path.targetTwitchUserId);
  }

  it('saves the typed line for the person and shows it', async () => {
    await openPage();
    await openUser('u1');

    button('[data-profile-shoutout-edit]').click();
    await settle();

    const field = document.body.querySelector<HTMLTextAreaElement>('[data-profile-shoutout-field] textarea')!;

    field.value = '  Go follow {name}!  ';
    field.dispatchEvent(new Event('input'));
    await settle();
    button('[data-form-action]').click();
    await settle();

    const puts = shoutoutWrites('PUT');

    expect(puts.map((c: Call) => c.body)).toEqual([
      {
        targetTwitchUserId: 'u1',
        targetDisplayName: 'Ada',
        messageTemplate: 'Go follow {name}!',
      },
    ]);

    expect(dialogs()[0]!.querySelector(OWN_LINE)!.textContent).toBe('Go follow {name}!');
  });

  it('removes the line only after the moderator confirms it', async () => {
    profiles.g1 = {
      ...profiles.g1,
      overrides: { shoutoutMessageTemplate: 'Go follow {name}!', ttsVoice: null },
    };

    await openPage();
    await openUser('u1');

    button('[data-profile-shoutout-remove]').click();
    await settle();

    expect(shoutoutWrites('DELETE')).toHaveLength(0);

    button('[data-confirm-action]').click();
    await settle();

    expect(shoutoutWrites('DELETE').map((c: Call) =>
      targetOf(c))).toEqual(['u1']);

    expect(dialogs()[0]!.querySelector(OWN_LINE)).toBeNull();
  });

  it('locks the edit for a caller without the shoutout right', async () => {
    held = held.filter((key: string) => key !== 'moderation:shoutout');
    await openPage();
    await openUser('u1');

    expect(button('[data-profile-shoutout-edit]').disabled).toBe(true);
  });
});

describe('the Emotes tab', () => {
  const groups = (): string[] =>
    Array.from(document.body.querySelectorAll<HTMLElement>('[data-profile-emote-group]'))
      .map((group: HTMLElement) => group.dataset.profileEmoteGroup ?? '');
  const emote = (code: string): HTMLElement =>
    document.body.querySelector(`[data-profile-emote="${code}"] [data-asset-trigger]`) as HTMLElement;

  it('asks for the person\'s own channel emotes and groups them by where they come from', async () => {
    await openPage();
    await openUser('u1');
    await openTab('Emotes');

    expect(calls.some((c: Call) =>
      c.path.endsWith('/community/{userId}/emotes') && userIdOf({ params: c.params } as Options) === 'g1'))
      .toBe(true);

    expect(groups()).toEqual(['Twitch', 'SevenTv']);

    expect(document.body.querySelector('[data-profile-emote="adaHi"] img')?.getAttribute('src'))
      .toBe('https://cdn/adaHi/1');
  });

  it('sorts the Twitch emotes into the tier that unlocks them', async () => {
    await openPage();
    await openUser('u1');
    await openTab('Emotes');

    const set = document.body.querySelector('[data-profile-emote="adaHi"]')!.closest<HTMLElement>('[data-profile-emote-set]');

    expect(set?.dataset.profileEmoteSet).toBe('tier1');
    expect(set?.textContent).toContain('Tier 1');
  });

  it('opens a card with the emote at its largest size, its tier as a number and its own page', async () => {
    await openPage();
    await openUser('u1');
    await openTab('Emotes');

    emote('adaHi').click();
    await settle();

    const card = document.body.querySelector('[data-asset-card]')!;

    expect(card.querySelector('img')?.getAttribute('src')).toBe('https://cdn/adaHi/3');
    expect(card.textContent).toContain('Tier 1');
    expect(card.textContent).not.toContain('1000');
    expect(card.querySelector('a')).toBeNull();
  });

  it('links a 7TV emote to its page on 7TV', async () => {
    await openPage();
    await openUser('u1');
    await openTab('Emotes');

    emote('adaDance').click();
    await settle();

    const link = document.body.querySelector<HTMLAnchorElement>('[data-asset-card] a');

    expect(link?.href).toBe('https://7tv.app/emotes/7d');
    expect(link?.textContent).toContain('Open on 7TV');
  });

  // Only an affiliate or a partner can have Twitch emotes; 7TV, BTTV and FFZ sets belong to anyone.
  it('shows no Twitch emotes for a regular channel and says why', async () => {
    (profiles.g1 as { identity: { broadcasterType: string } }).identity.broadcasterType = '';
    await openPage();
    await openUser('u1');
    await openTab('Emotes');

    const active = dialogs()[0]!.querySelector('[role="tabpanel"][data-state="active"]');

    expect(groups()).toEqual(['SevenTv']);
    expect(active?.textContent).toContain('A regular Twitch channel has no Twitch emotes.');
  });

  it('says the person has no channel emotes when the bot finds none', async () => {
    await openPage();
    await openUser('u2');
    await openTab('Emotes');

    const active = dialogs()[0]!.querySelector('[role="tabpanel"][data-state="active"]');

    expect(groups()).toEqual([]);
    expect(active?.textContent).toContain('No channel emotes found for this person.');
  });
});

describe('the public profile for a caller who cannot read the community', () => {
  it('shows who the person is, their chat and emotes, and no moderator controls', async () => {
    held = [];
    await openPage();
    await openUser('u1');

    const text = dialogs()[0]!.textContent ?? '';

    expect(text).toContain('Ada');
    expect(text).toContain('she/her');
    expect(document.body.querySelector('[data-slot="profile-public"]')).not.toBeNull();

    expect(calls.some((c: Call) =>
      c.path.endsWith('/chat/viewer/users/{userId}/profile'))).toBe(true);

    expect(calls.some((c: Call) =>
      c.path.endsWith('/community/{userId}'))).toBe(false);

    expect(document.body.querySelector('[data-profile-full-page]')).toBeNull();
    expect(document.body.querySelector('[data-profile-vip]')).toBeNull();
    expect(text).not.toContain('Moderation');

    await openTab('Chat');
    expect(dialogs()[0]!.textContent).toContain('public hello from ada');

    await openTab('Emotes');
    expect(document.body.querySelector('[data-profile-emote="adaHi"]')).not.toBeNull();
  });
});
