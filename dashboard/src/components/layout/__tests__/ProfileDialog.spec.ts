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

import { onTheWire } from '@/api/__tests__/wire';
import ProfileDialog from '@/components/layout/ProfileDialog.vue';
import UserName from '@/components/layout/UserName.vue';
import { i18n } from '@/i18n';
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

// A person's own channel emotes by Twitch id (what GET community/{userId}/emotes answers).
const emotes: Record<string, Record<string, unknown>[]> = {
  u1: [
    {
      code: 'adaHi',
      provider: 'Twitch',
      urls: { 1: 'https://cdn/adaHi/1', 3: 'https://cdn/adaHi/3' },
      animated: false,
      zeroWidth: false,
      setId: 's1',
      id: 'e1',
      type: 'subscriptions',
      tier: '1000',
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
  'moderation:ban',
  'moderation:unban',
  'moderation:vip',
  'moderation:shoutout',
  'moderation:usercontext:read',
  'moderation:note:write',
  'moderation:history:read',
  'moderation:warn',
] as const;

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

vi.mock('@/api/client', () => ({
  client: {
    GET: vi.fn<(path: string, options?: Options) => Promise<unknown>>(async (path, options) => {
      record(path, options);

      if (path.endsWith('/profile'))
        return { data: { data: onTheWire(profiles[userIdOf(options)] ?? null) } };

      if (path.endsWith('/chat/emotes')) {
        return {
          data: { data: onTheWire(channelEmotes) },
          response: { ok: true, status: 200 },
        };
      }

      if (path.endsWith('/roles/effective/me'))
        return { data: { data: { heldActionKeys: [...held] } }, response: { ok: true } };

      if (path.endsWith('/history/{userId}'))
        return { data: historyPage(options), response: { ok: true } };

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

      return { response: { ok: true } };
    }),
    DELETE: vi.fn<(path: string, options?: Options) => Promise<unknown>>(async (path, options) => {
      record(path, options);
      people[userIdOf(options)]!.isVip = false;

      return { response: { ok: true } };
    }),
  },
}));

// The login of the channel being managed; a test sets it to make a person the channel's own account.
// The live channel list sends own channels without a Twitch id, so the login is what identifies them.
const channelLogin = ref('somebody');

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({
    channelId: ref('c1'),
    current: computed(() => ({ login: channelLogin.value, twitchId: null })),
  }),
}));

let mounted: VueWrapper | null = null;
let pinia: Pinia;

// jsdom has no layout: the chat list needs a viewport and row heights to render any rows.
beforeEach(() => {
  calls.length = 0;
  channelLogin.value = 'somebody';
  people = freshPeople();
  profiles = freshProfiles();
  notes = freshNotes();
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

// The VIP choice lives in the More menu; this opens the menu when it is closed.
async function vipItem(): Promise<HTMLElement> {
  if (!document.body.querySelector('[data-profile-vip]')) {
    openMenu(button('[data-profile-more]'));
    await settle();
  }

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
    ]);

    tabs[1]!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }));
    await settle();

    const active = dialogs()[0]!.querySelector('[role="tabpanel"][data-state="active"]');

    expect(active?.textContent).toContain('hello from ada');
  });

  it('keeps every tab panel mounted in one cell, so the dialog takes the tallest tab size up front', async () => {
    await openPage();
    await openUser('u1');

    const panels: HTMLElement[] = Array.from(dialogs()[0]!.querySelectorAll('[role="tabpanel"]'));

    expect(panels).toHaveLength(6);

    for (const panel of panels) {
      expect(panel.hidden).toBe(false);
      expect(panel.classList).toContain('row-start-1');
      expect(panel.classList).toContain('col-start-1');
    }

    expect(panels.filter((panel) => panel.dataset.state === 'inactive').every((panel) =>
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
  });

  it('lists the ban history of a banned person on the Moderation tab', async () => {
    await openPage();
    await openUser('u2');
    await openTab('Moderation');

    const active = dialogs()[0]!.querySelector('[role="tabpanel"][data-state="active"]');

    expect(active?.textContent).toContain('spam links');
    expect(active?.textContent).toContain('Stoney_Eagle');
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
      '/history/{userId}',
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

    expect(item.getAttribute('aria-disabled')).toBe('true');
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

  it('shouts a person out from the More menu', async () => {
    await openPage();
    await openUser('u1');

    openMenu(button('[data-profile-more]'));
    await settle();
    button('[data-profile-shoutout]').click();
    await settle();

    const shoutouts = calls.filter((c: Call) => c.path.endsWith('/moderation/shoutout'));

    expect(shoutouts.map((c: Call) => c.body)).toEqual([{ targetTwitchUserId: 'u1' }]);
  });

  it('warns the person from More with the reason the moderator typed', async () => {
    await openPage();
    await openUser('u1');

    openMenu(button('[data-profile-more]'));
    await settle();
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

    openMenu(button('[data-profile-more]'));
    await settle();

    const warn = document.body.querySelector<HTMLElement>('[data-profile-warn]')!;

    expect(warn.hasAttribute('data-disabled')).toBe(true);
    expect(warn.getAttribute('title')).toBe(i18n.global.t('common.readOnly'));
  });

  it('offers Unban instead of Ban and Timeout for a banned person, and lifts the ban', async () => {
    await openPage();
    await openUser('u2');

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

    expect((await vipItem()).getAttribute('aria-disabled')).toBeNull();
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
});

const header = (): HTMLElement => document.body.querySelector('[data-slot="profile-header"]')!;
const overview = (): HTMLElement => document.body.querySelector('[data-slot="profile-overview"]')!;
const chatLines = (): string[] =>
  Array.from(overview().querySelectorAll('[data-profile-line]')).map(
    (line: Element) => line.textContent ?? '',
  );

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
    expect(text).toContain('Last seen 3 days ago');
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

  it('marks the subscriber role with the Twitch logo alone', async () => {
    await openPage();
    await openUser('u1');

    const subscriber = Array.from(header().querySelectorAll('[data-profile-role]')).find(
      (badge: Element) => badge.textContent?.trim() === 'Subscriber',
    )!;

    expect(subscriber.querySelectorAll('svg')).toHaveLength(1);
    expect(subscriber.querySelector('[data-profile-platform]')).not.toBeNull();
  });

  // Mockup profile.js .stats: the number on top and its label under it, so a label that wraps never pushes
  // its number out of line with the others.
  it('shows a stat for each number the API returns, the number above its label', async () => {
    await openPage();
    await openUser('u1');

    const stats = Array.from(header().querySelectorAll('[data-profile-stat]')).map(
      (tile: Element) => Array.from(tile.children).map((part: Element) => part.textContent).join(' '),
    );

    expect(stats).toEqual([
      '1,234 Chat lines',
      '12.5 Hours watched',
      '7 Commands used',
      '9 Rewards redeemed',
      '4 Watch streak',
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
  it('lists the Identity and Relationship facts from the profile', async () => {
    await openPage();
    await openUser('u1');

    const text = overview().textContent ?? '';

    expect(text).toContain('Identity');
    expect(text).toContain('Relationship');
    expect(text).toContain('she/her');
    expect(text).toContain('90210');
    // memberSinceUtc is when the channel role was given (ChannelMembership.GrantedAt), not the account date.
    expect(text).toContain('Role since');
    expect(text).toContain('Mar 4, 2020');
    expect(text).toContain('Following');
    expect(text).toContain('Yes');
    expect(text).toContain('Tier 1');
    expect(text).toContain('Jan 2, 2026');
  });

  it('shows the Twitch account facts: its age, status, nickname, color, time zone and bits', async () => {
    await openPage();
    await openUser('u1');

    const text = overview().textContent ?? '';
    const swatch = overview().querySelector<HTMLElement>('[data-profile-color]');

    expect(text).toContain('Account created');
    expect(text).toContain('Jun 1, 2015');
    expect(text).toContain('Twitch affiliate, Twitch staff');
    expect(text).toContain('Countess');
    expect(text).toContain('Writes compilers for fun.');
    expect(text).toContain('Europe/London');
    expect(text).toContain('she/her (set by the channel)');
    expect(text).toContain('Kick: AdaK, linked May 6, 2026');
    expect(text).toContain('Lurking');
    expect(text).toContain('Bits cheered');
    expect(text).toContain('500');
    expect(swatch?.style.backgroundColor).toBe('rgb(255, 127, 80)');
  });

  it('shows since when the person follows the channel, with its age', async () => {
    await openPage();
    await openUser('u1');

    const text = overview().textContent ?? '';

    expect(text).toContain('Following');
    expect(text).toContain('Since Aug 15, 2024 (');
  });

  it('omits a fact the server does not return', async () => {
    await openPage();
    await openUser('u2');

    const labels = Array.from(overview().querySelectorAll('dt')).map(
      (dt: Element) => dt.textContent,
    );

    expect(labels).not.toContain('Pronouns');
    expect(labels).not.toContain('Role since');
    expect(labels).not.toContain('Following');
    expect(labels).not.toContain('Twitch ID');
    expect(labels).not.toContain('Subscription');

    // An empty Twitch status, no color and not lurking are not facts worth a row.
    for (const label of ['Account created', 'Nickname', 'About', 'Time zone', 'Chat color', 'On Twitch', 'Lurking', 'Bits cheered']) {
      expect(labels).not.toContain(label);
    }

    expect(labels).toContain('First seen');
  });

  it('shows the newest 3 chat lines and opens the Chat tab from All chat', async () => {
    people.u1!.recentActivity = [1, 2, 3, 4].map((minute: number) => ({
      type: 'chat',
      content: `line ${minute}`,
      timestamp: `2026-10-01T10:0${minute}:00Z`,
    }));

    await openPage();
    await openUser('u1');

    const shown = chatLines();

    expect(shown.length).toBe(3);
    expect(shown[0]).toContain('line 4');
    expect(shown[2]).toContain('line 2');
    expect(shown.join(' ')).not.toContain('line 1');
    // Another day reads in the same date style as the rest of the dialog, never as 2026-10-01.
    expect(shown.join(' ')).toContain('Oct 1');
    expect(shown.join(' ')).not.toContain('2026-10-01');

    button('[data-profile-all-chat]').click();
    await settle();

    const chatTab = Array.from(document.body.querySelectorAll('[role="tab"]')).find(
      (t: Element) => t.textContent?.trim() === 'Chat',
    );

    expect(chatTab?.getAttribute('aria-selected')).toBe('true');

    expect(
      document.body.querySelector('[data-slot="profile-overview"]')!.closest('[role="tabpanel"]')!
        .getAttribute('data-state'),
    ).toBe('inactive');
  });

  it('shows a known emote code in a chat line as the emote, and keeps other words as text', async () => {
    people.u1!.recentActivity = [{
      type: 'chat',
      content: 'nice raid stoney90Hmmm stoney90Unknown',
      timestamp: '2026-10-01T10:00:00Z',
    }];

    await openPage();
    await openUser('u1');

    for (const scope of [overview(), dialogs()[0]!.querySelector('[data-profile-chat-lines]')!]) {
      const emote = scope.querySelector<HTMLImageElement>('img[data-emote="stoney90Hmmm"]');

      expect(emote?.src).toBe('https://cdn.example/hmmm/1x.png');
      expect(emote?.alt).toBe('stoney90Hmmm');
      expect(scope.textContent).toContain('nice raid');
      expect(scope.textContent).toContain('stoney90Unknown');
    }
  });

  it('says there are no chat lines and offers no All chat button when there are none', async () => {
    await openPage();
    await openUser('u2');

    expect(chatLines()).toEqual([]);
    expect(overview().textContent).toContain('No chat lines stored yet.');
    expect(document.body.querySelector('[data-profile-all-chat]')).toBeNull();
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
      c.path.endsWith('/community/{userId}/emotes') && userIdOf({ params: c.params } as Options) === 'u1'))
      .toBe(true);

    expect(groups()).toEqual(['Twitch', 'SevenTv']);

    expect(document.body.querySelector('[data-profile-emote="adaHi"] img')?.getAttribute('src'))
      .toBe('https://cdn/adaHi/1');
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

  it('says the person has no channel emotes when the bot finds none', async () => {
    await openPage();
    await openUser('u2');
    await openTab('Emotes');

    const active = dialogs()[0]!.querySelector('[role="tabpanel"][data-state="active"]');

    expect(groups()).toEqual([]);
    expect(active?.textContent).toContain('No channel emotes found for this person.');
  });
});
