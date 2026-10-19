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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, ref } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { i18n } from '@/i18n';

import ProfileMenu from '../ProfileMenu.vue';

const user = ref({
  displayName: 'Streamer',
  profileImageUrl: null,
  isAdmin: false,
});

vi.mock('@/api/auth', () => ({
  useCurrentUser: () => ({ data: user }),
  signOut: vi.fn<() => Promise<void>>(async () => undefined),
}));

// The menu of the owner of the channel: the only one that can preview a smaller role.
vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({ current: ref({ kind: 'own' }) }),
}));

// Stoney 2026-10-20 ("need i say more about this?"): the account menu is a real menu like the sidebar user menu of
// shadcn: a label with the name, plain items, a Language submenu and no underlined links.
const page = defineComponent({ render: () => h('div') });
let wrapper: VueWrapper | null = null;

// jsdom has no Fullscreen API. VueUse needs a request method, an exit method and a fullscreen state flag to call the
// browser "supported", so this stands in for Chrome: entering sets the element, exiting clears it, both fire the event.
let fullscreenElement: Element | null = null;

function changeFullscreenElement(next: Element | null): void {
  fullscreenElement = next;
  document.dispatchEvent(new Event('fullscreenchange'));
}

function installFullscreenApi(): void {
  fullscreenElement = null;

  Object.defineProperty(document, 'fullscreenElement', { get: () => fullscreenElement, configurable: true });
  Object.defineProperty(document, 'webkitIsFullScreen', { get: () => fullscreenElement !== null, configurable: true });

  Object.defineProperty(document, 'exitFullscreen', {
    value: vi.fn<() => Promise<void>>(async () => changeFullscreenElement(null)),
    configurable: true,
  });

  const root = document.documentElement;

  Object.defineProperty(root, 'requestFullscreen', {
    value: vi.fn<() => Promise<void>>(async () => changeFullscreenElement(root)),
    configurable: true,
  });
}

function removeFullscreenApi(): void {
  Reflect.deleteProperty(document, 'fullscreenElement');
  Reflect.deleteProperty(document, 'webkitIsFullScreen');
  Reflect.deleteProperty(document, 'exitFullscreen');
  Reflect.deleteProperty(document.documentElement, 'requestFullscreen');
}

function mountMenu(): void {
  const router: Router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        name: 'dashboard-settings',
        path: '/settings',
        component: page,
      },
      {
        name: 'dashboard-admin-channels',
        path: '/admin/channels',
        component: page,
      },
      {
        name: 'login',
        path: '/login',
        component: page,
      },
    ],
  });

  wrapper = mount(ProfileMenu, {
    attachTo: document.body,
    global: { plugins: [i18n, router] },
  });
}

async function clickTrigger(): Promise<void> {
  await wrapper!.get('[data-slot="profile-menu"]').trigger('click');
  await flushPromises();
}

async function openMenu(): Promise<void> {
  mountMenu();
  await clickTrigger();
}

function items(): string[] {
  const nodes = document.body.querySelectorAll('[role="menuitem"], [role="menuitemradio"]');

  return [...nodes].map((node) => node.textContent?.trim() ?? '');
}

function fullscreenEntry(): HTMLElement | null {
  return document.body.querySelector<HTMLElement>('[data-slot="fullscreen-toggle"]');
}

beforeEach(installFullscreenApi);

afterEach(() => {
  wrapper?.unmount();
  wrapper = null;
  document.body.innerHTML = '';

  user.value = {
    displayName: 'Streamer',
    profileImageUrl: null,
    isAdmin: false,
  };

  removeFullscreenApi();
});

describe('the account menu', () => {
  it('is a menu: the name as its label, then the entries in order with Sign out last', async () => {
    await openMenu();

    expect(document.body.querySelector('[role="menu"]')).not.toBeNull();
    expect(document.body.querySelector('[data-slot="dropdown-menu-label"]')?.textContent).toBe('Streamer');

    expect(items()).toEqual([
      'Readability',
      'View as moderator',
      'View as viewer',
      'Language',
      'Fullscreen',
      'Sign out',
    ]);
  });

  // styles/frame.css underlines every `a:not([data-slot])`. jsdom loads no stylesheet, so the test checks the selector
  // itself: a link entry keeps its menu-item slot, and the underline rule skips it.
  it('keeps the slot on every link entry, so the page link underline skips it', async () => {
    user.value = {
      displayName: 'Streamer',
      profileImageUrl: null,
      isAdmin: true,
    };

    await openMenu();

    const links = [...document.body.querySelectorAll('[role="menu"] a')];

    expect(links.map((link) => link.textContent?.trim())).toEqual(['Readability', 'Platform admin']);
    expect(document.body.querySelectorAll('[role="menu"] a:not([data-slot])')).toHaveLength(0);
  });

  it('asks the page to go fullscreen, offers to leave once it did, and leaves on the second pick', async () => {
    await openMenu();

    expect(fullscreenEntry()?.textContent?.trim()).toBe('Fullscreen');

    fullscreenEntry()!.click();
    await flushPromises();

    expect(document.documentElement.requestFullscreen).toHaveBeenCalledTimes(1);
    expect(fullscreenElement).toBe(document.documentElement);

    await clickTrigger();

    expect(fullscreenEntry()?.textContent?.trim()).toBe('Exit fullscreen');

    fullscreenEntry()!.click();
    await flushPromises();

    expect(document.exitFullscreen).toHaveBeenCalledTimes(1);
    expect(fullscreenElement).toBeNull();
  });

  it('offers no fullscreen entry where the browser has no Fullscreen API', async () => {
    removeFullscreenApi();
    await openMenu();

    expect(fullscreenEntry()).toBeNull();
    expect(items()).toEqual(['Readability', 'View as moderator', 'View as viewer', 'Language', 'Sign out']);
  });
});
