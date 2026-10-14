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

// The viewer profile by internal id (what GET community/{userId}/profile answers).
let profiles: Record<string, Record<string, unknown> | null> = {};

const freshProfiles = (): Record<string, Record<string, unknown> | null> => ({
  g1: {
    identity: {
      userId: 'g1',
      twitchUserId: '90210',
      username: 'ada',
      displayName: 'Ada',
      profileImageUrl: null,
      pronoun: 'she/her',
      altPronoun: null,
      linkedIdentities: [],
      communityStanding: 'viewer',
      // The live API sends the Twitch tier code, never a label.
      subTier: '1000',
      managementRole: 'moderator',
      memberSinceUtc: '2020-03-04T00:00:00Z',
      firstSeenUtc: '2026-01-02T10:00:00Z',
    },
    activity: {
      totalMessages: 1234,
      totalRedemptions: 9,
      isFollower: true,
      isSubscriber: true,
      subTier: '1000',
    },
    streak: {
      currentStreak: 4,
      maxStreak: 6,
      lastSeenDate: '2026-10-01',
    },
  },
  g2: {
    identity: {
      userId: 'g2',
      twitchUserId: null,
      username: 'bo',
      displayName: 'Bo',
      profileImageUrl: null,
      pronoun: null,
      altPronoun: null,
      linkedIdentities: [],
      communityStanding: 'viewer',
      subTier: null,
      managementRole: null,
      memberSinceUtc: null,
      firstSeenUtc: '2026-02-02T10:00:00Z',
    },
    activity: null,
    streak: null,
  },
});

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
        return { data: { data: profiles[userIdOf(options)] ?? null } };

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
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date('2026-10-04T10:00:00Z'));
  vipWrite = null;
  vipWriteFails = false;

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
      'Moderation',
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

    expect(panels).toHaveLength(3);

    for (const panel of panels) {
      expect(panel.hidden).toBe(false);
      expect(panel.classList).toContain('row-start-1');
      expect(panel.classList).toContain('col-start-1');
    }

    expect(panels.filter((panel) => panel.dataset.state === 'inactive').every((panel) =>
      panel.classList.contains('data-[state=inactive]:invisible'))).toBe(true);
  });

  it('lists the ban history of a banned person on the Moderation tab', async () => {
    await openPage();
    await openUser('u2');

    const tabs: HTMLElement[] = Array.from(document.body.querySelectorAll('[role="tab"]'));

    tabs[2]!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, button: 0 }));
    await settle();

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

  it('flips the VIP button at once and keeps it when the save works', async () => {
    await openPage();
    await openUser('u1');
    expect(button('[data-profile-vip]').textContent).toContain('Make VIP');

    let release: () => void = () => {};

    vipWrite = new Promise<void>((done) => {
      release = done;
    });

    button('[data-profile-vip]').click();
    await flushPromises();
    expect(button('[data-profile-vip]').textContent).toContain('Remove VIP');

    release();
    await settle();
    expect(button('[data-profile-vip]').textContent).toContain('Remove VIP');
    expect(calls.some((c: Call) => c.path.endsWith('/vip'))).toBe(true);
  });

  it('rolls the VIP button back and tells the error inbox when the save fails', async () => {
    await openPage();
    await openUser('u1');
    vipWriteFails = true;

    button('[data-profile-vip]').click();
    await settle();

    expect(button('[data-profile-vip]').textContent).toContain('Make VIP');
    expect(useSaveErrorsStore().items.length).toBe(1);
  });

  it('takes the VIP state from the person Twitch was asked about, not from a VIP list', async () => {
    people.u1!.isVip = true;
    await openPage();
    await openUser('u1');

    expect(button('[data-profile-vip]').textContent).toContain('Remove VIP');
    const writes = calls.filter(
      (c: Call) => !c.path.endsWith('/community/{userId}') && !c.path.endsWith('/profile'),
    );

    expect(writes).toEqual([]);
  });

  it('says the VIP state is unknown and offers no VIP change when Twitch could not be asked', async () => {
    await openPage();
    await openUser('u2');

    expect(button('[data-profile-vip]').disabled).toBe(true);
    expect(button('[data-profile-vip]').textContent).toContain('VIP status unknown');
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
    expect(text).not.toContain('Account created');
    expect(text).toContain('Mar 4, 2020');
    expect(text).toContain('Following');
    expect(text).toContain('Yes');
    expect(text).toContain('Tier 1');
    expect(text).toContain('Jan 2, 2026');
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

  it('says there are no chat lines and offers no All chat button when there are none', async () => {
    await openPage();
    await openUser('u2');

    expect(chatLines()).toEqual([]);
    expect(overview().textContent).toContain('No chat lines stored yet.');
    expect(document.body.querySelector('[data-profile-all-chat]')).toBeNull();
  });
});
