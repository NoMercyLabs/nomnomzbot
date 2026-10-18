// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, ref } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { setWidth } from '@/__tests__/viewport';
import { i18n } from '@/i18n';
import DashboardLayout from '@/layouts/DashboardLayout.vue';
import ProfileMenu from '@/layouts/dashboard/ProfileMenu.vue';

const user = ref<{
  displayName: string;
  profileImageUrl: string | null;
  isAdmin: boolean;
}>({
  displayName: 'Streamer',
  profileImageUrl: null,
  isAdmin: false,
});

vi.mock('@/api/auth', () => ({
  useCurrentUser: () => ({ data: user }),
  signOut: vi.fn<() => Promise<void>>(),
  // Signed out for the live hubs: these tests cover the frame, not live updates.
  useSignedIn: () => ref(false),
  getAccessToken: () => '',
}));

type Suspension = {
  status: string;
  reason: string | null;
  suspendedAt: string | null;
  contactUrl: string | null;
};

const current = ref<{
  chatColor: string | null;
  displayName?: string;
  suspension?: Suspension | null;
} | null>(null);

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({
    channels: ref([]),
    current,
    channelId: ref(null),
    isPending: ref(false),
    isLoaded: ref(true),
    choose: vi.fn<() => Promise<void>>(),
  }),
}));

// The intent prefetch has its own spec; these tests cover the frame, which has no query client here.
vi.mock('@/composables/useIntentPrefetch', () => ({ useIntentPrefetch: () => undefined }));
vi.mock('../useReadableRedirect', () => ({ useReadableRedirect: () => undefined }));

const stub = (slot: string) => defineComponent({ render: () => h('div', { 'data-stub': slot }) });

function mountLayout(width: number): VueWrapper {
  setWidth(width);

  const framed: Router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/app/u/:channelName/:rest*', component: stub('page') },
      { path: '/app/:rest(.*)*', component: stub('page') },
    ],
  });

  return mount(DashboardLayout, {
    global: {
      plugins: [i18n, framed],
      stubs: {
        DashboardSidebar: stub('sidebar'),
        NavRail: stub('rail'),
        BottomBar: stub('bottom'),
        ChannelSwitch: stub('switch'),
        ShieldToggle: stub('shield'),
        ErrorInbox: stub('inbox'),
        ProfileMenu: stub('profile'),
        RouterView: stub('view'),
      },
    },
  });
}

function frames(wrapper: VueWrapper): string[] {
  return wrapper.findAll('[data-stub]').map((node) => node.attributes('data-stub') as string);
}

afterEach(() => {
  document.body.innerHTML = '';

  user.value = {
    displayName: 'Streamer',
    profileImageUrl: null,
    isAdmin: false,
  };

  current.value = null;
  document.documentElement.removeAttribute('style');
});

describe('the theme of the frame', () => {
  it('paints the accent in the color of the chosen channel, and follows a switch', async () => {
    current.value = { chatColor: '#E05A00' };
    mountLayout(1000);
    await flushPromises();

    expect(document.documentElement.style.getPropertyValue('--primary')).toBe('rgb(224 90 0)');

    current.value = { chatColor: '#00AA55' };
    await flushPromises();

    expect(document.documentElement.style.getPropertyValue('--primary')).not.toBe('rgb(224 90 0)');

    expect(document.documentElement.style.getPropertyValue('--ring')).toBe(
      document.documentElement.style.getPropertyValue('--primary'),
    );
  });

  it('keeps the approved pink for a channel without a color', async () => {
    current.value = { chatColor: null };
    mountLayout(1000);
    await flushPromises();

    expect(document.documentElement.style.getPropertyValue('--primary')).toBe('rgb(255 1 79)');
  });
});

describe('the dashboard frame by window width', () => {
  it('shows the sidebar from 840 px and no rail or bottom bar', () => {
    const shown = frames(mountLayout(1000));

    expect(shown).toContain('sidebar');
    expect(shown).not.toContain('rail');
    expect(shown).not.toContain('bottom');
  });

  it('puts no bar above the page on a wide screen, so the page header is the only header', () => {
    const wrapper = mountLayout(1000);

    expect(wrapper.find('[data-slot="frame-header"]').exists()).toBe(false);
    expect(frames(wrapper)).not.toContain('shield');
  });

  it('shows the icon rail between 600 and 839 px', () => {
    const shown = frames(mountLayout(700));

    expect(shown).toContain('rail');
    expect(shown).not.toContain('sidebar');
    expect(shown).not.toContain('bottom');
  });

  it('shows the bottom bar under 600 px, with the channel switch in the header', () => {
    const shown = frames(mountLayout(400));

    expect(shown).toContain('bottom');
    expect(shown).toContain('switch');
    expect(shown).not.toContain('sidebar');
    expect(shown).not.toContain('rail');
  });
});

describe('a link to a channel the person cannot manage', () => {
  it('shows the no-access state instead of the page', async () => {
    const wrapper = mountLayout(1000);

    await wrapper.vm.$router.push('/app/u/stranger/stream');
    await flushPromises();

    const state = wrapper.find('[data-slot="channel-no-access"]');

    expect(state.exists()).toBe(true);
    expect(state.text()).toContain('stranger');
    expect(frames(wrapper)).not.toContain('view');
  });

  it('shows the page on the short route', async () => {
    const wrapper = mountLayout(1000);

    await flushPromises();

    expect(wrapper.find('[data-slot="channel-no-access"]').exists()).toBe(false);
    expect(frames(wrapper)).toContain('view');
  });
});

describe('a channel the platform suspended', () => {
  const suspended = {
    status: 'suspended',
    reason: 'Spam reports',
    suspendedAt: '2026-10-18T10:00:00Z',
    contactUrl: 'https://support.example.org/contact',
  } as const satisfies Suspension;

  it('shows the suspended view with the reason and the contact link on load, and no navigation', async () => {
    current.value = {
      chatColor: null,
      displayName: 'Streamer',
      suspension: suspended,
    };

    const wrapper = mountLayout(1000);

    await flushPromises();

    const state = wrapper.find('[data-slot="channel-suspended"]');

    expect(state.exists()).toBe(true);
    expect(state.text()).toContain('Streamer');
    expect(wrapper.find('[data-slot="suspension-reason"]').text()).toBe('Spam reports');
    expect(wrapper.find('[data-slot="suspension-contact"]').attributes('href')).toBe(suspended.contactUrl);
    expect(frames(wrapper)).not.toContain('view');
    expect(frames(wrapper)).not.toContain('sidebar');
  });

  it('enters the suspended view live when the channel row turns suspended', async () => {
    current.value = {
      chatColor: null,
      displayName: 'Streamer',
      suspension: null,
    };

    const wrapper = mountLayout(1000);

    await flushPromises();

    expect(frames(wrapper)).toContain('view');

    current.value = {
      chatColor: null,
      displayName: 'Streamer',
      suspension: suspended,
    };

    await flushPromises();

    expect(wrapper.find('[data-slot="channel-suspended"]').exists()).toBe(true);
    expect(frames(wrapper)).not.toContain('view');
  });

  it('leaves the suspended view live when the channel is reinstated', async () => {
    current.value = {
      chatColor: null,
      displayName: 'Streamer',
      suspension: suspended,
    };

    const wrapper = mountLayout(1000);

    await flushPromises();

    current.value = {
      chatColor: null,
      displayName: 'Streamer',
      suspension: null,
    };

    await flushPromises();

    expect(wrapper.find('[data-slot="channel-suspended"]').exists()).toBe(false);
    expect(frames(wrapper)).toContain('view');
    expect(frames(wrapper)).toContain('sidebar');
  });

  it('says who to ask instead of linking when no support contact is set', async () => {
    current.value = {
      chatColor: null,
      displayName: 'Streamer',
      suspension: {
        ...suspended,
        contactUrl: null,
      },
    };

    const wrapper = mountLayout(1000);

    await flushPromises();

    expect(wrapper.find('[data-slot="suspension-contact"]').exists()).toBe(false);
    expect(wrapper.find('[data-slot="suspension-no-contact"]').exists()).toBe(true);
  });
});

describe('the profile menu', () => {
  const page = defineComponent({ render: () => h('div') });

  async function openMenu(isAdmin: boolean): Promise<string> {
    user.value = {
      displayName: 'Streamer',
      profileImageUrl: null,
      isAdmin,
    };

    setWidth(1000);
    const router: Router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          name: 'dashboard-admin-channels',
          path: '/admin',
          component: page,
        },
        {
          name: 'dashboard-settings',
          path: '/settings',
          component: page,
        },
      ],
    });
    const wrapper: VueWrapper = mount(ProfileMenu, {
      attachTo: document.body,
      global: { plugins: [i18n, router] },
    });

    await wrapper.find('[data-slot="profile-menu"]').trigger('click');
    await flushPromises();

    return document.body.textContent ?? '';
  }

  it('offers Platform admin to an admin', async () => {
    expect(await openMenu(true)).toContain('Platform admin');
  });

  it('does not offer Platform admin to anyone else', async () => {
    const text = await openMenu(false);

    expect(text).toContain('Sign out');
    expect(text).not.toContain('Platform admin');
  });
});
