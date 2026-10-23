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
import { computed, defineComponent, h, ref } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { i18n } from '@/i18n';

import BottomBar from '../BottomBar.vue';
import { dashboardNav } from '../navigation';

// The navigation asks the bot what the person may read; these tests cover the frame, so nothing is known yet and nothing is hidden.
// The bot has not told the platform keys yet, so no admin entry is hidden.
vi.mock('@/composables/usePlatformCan', () => ({
  usePlatformCan: () => ({ known: { value: false }, can: () => false }),
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  useCurrentChannel: () => ({
    channelId: ref(null),
    current: ref(null),
  }),
}));

vi.mock('@/api/access', () => ({
  useChannelAccess: () => ({ known: computed(() => false), can: () => false }),
}));

const page = defineComponent({ render: () => h('div') });

let mounted: VueWrapper | undefined;

afterEach(() => {
  mounted?.unmount();
  document.body.innerHTML = '';
});

function makeRouter(): Router {
  const names = dashboardNav.flatMap((group) => group.entries.map((entry) => entry.route));

  return createRouter({
    history: createMemoryHistory(),
    routes: names.map((name) => ({
      name,
      path: `/${name}`,
      component: page,
    })),
  });
}

async function openMore(path: string): Promise<void> {
  const router = makeRouter();

  await router.push(path);

  mounted = mount(BottomBar, {
    attachTo: document.body,
    global: { plugins: [router, i18n] },
  });

  await flushPromises();

  const trigger = mounted.findAll('button').find((button) => button.text() === 'More');

  await trigger!.trigger('click');
  await flushPromises();
}

function sheet(): HTMLElement {
  return document.body.querySelector<HTMLElement>('[data-slot="more-drawer"]')!;
}

// Stoney 2026-10-15 audit: on a phone the More menu had no close control and two rows looked highlighted.
describe('the bottom bar More menu', () => {
  it('has a visible close control that closes the sheet', async () => {
    await openMore('/dashboard-analytics');

    const close = sheet().querySelector<HTMLElement>('[data-drawer-close]');

    expect(close).not.toBeNull();
    expect(close!.getAttribute('aria-label')).toBe('Close');

    close!.click();
    await flushPromises();

    expect(document.body.querySelector('[data-slot="more-drawer"]')).toBeNull();
  });

  // Stoney 2026-10-21 (mobile ledger M-10): More kept a focus ring after the sheet closed on a phone.
  it('leaves the More tab without focus after a touch close, so no ring shows', async () => {
    await openMore('/dashboard-analytics');

    window.dispatchEvent(new Event('pointerdown'));

    sheet().querySelector<HTMLElement>('[data-drawer-close]')!.click();
    await flushPromises();
    await new Promise((done) => setTimeout(done, 50));

    const more = mounted!.findAll('button').find((button) => button.text() === 'More');

    expect(document.activeElement).not.toBe(more!.element);
  });

  // Stoney 2026-10-21 (mobile ledger M-11): the sheet listed every page in one column with no groups.
  it('names the groups of its pages, each page under its own group', async () => {
    await openMore('/dashboard-analytics');

    const groups = Array.from(sheet().querySelectorAll('[data-slot="more-group"]'));
    const headings = groups.map((group) => group.querySelector('[data-slot="more-group-label"]')?.textContent);

    expect(headings.length).toBeGreaterThan(1);
    expect(headings).toContain('Build');

    const build = groups.find((group) => group.textContent?.startsWith('Build'));

    expect(build!.querySelectorAll('a').length).toBeGreaterThan(1);

    expect(sheet().querySelectorAll('a').length).toBe(
      groups.reduce((total, group) => total + group.querySelectorAll('a').length, 0),
    );
  });

  it('marks only the row of the current page, and focus does not land on another row', async () => {
    await openMore('/dashboard-analytics');

    const current = sheet().querySelectorAll('[aria-current="page"]');
    const shaded = sheet().querySelectorAll('[data-slot="item"].bg-muted');

    expect(current).toHaveLength(1);
    expect(current[0]!.textContent).toContain('Analytics');
    expect(shaded).toHaveLength(1);
    expect(document.activeElement?.closest('a')).toBeNull();
  });
});

// Stoney 2026-10-19 Webhooks acceptance at 390: labels were cut on both sides ("asics", "Mor").
// A flex item keeps its text width unless min-w-0 lets it shrink, so the label can only truncate with an ellipsis when its tab and its span may shrink.
describe('the bottom bar tabs', () => {
  // Stoney 2026-10-23: the More tab's icon had no size, so it was smaller than the others and its label sat
  // higher than the rest of the labels.
  it('draws every tab icon at one size so the labels share a baseline', async () => {
    const router = makeRouter();

    await router.push('/dashboard-analytics');

    mounted = mount(BottomBar, {
      attachTo: document.body,
      global: { plugins: [router, i18n] },
    });

    await flushPromises();

    const icons = mounted.findAll('nav > a svg, nav > button svg');

    expect(icons.length).toBeGreaterThan(1);
    expect(icons.map((icon) => icon.classes().includes('size-5'))).toEqual(icons.map(() => true));
  });

  it('lets every tab and label shrink so a long label ends in an ellipsis instead of spilling', async () => {
    const router = makeRouter();

    await router.push('/dashboard-analytics');

    mounted = mount(BottomBar, {
      attachTo: document.body,
      global: { plugins: [router, i18n] },
    });

    await flushPromises();

    const tabs = mounted.findAll('nav > a, nav > button');

    expect(tabs.length).toBeGreaterThan(1);

    const tabsThatCannotShrink = tabs
      .filter((tab) =>
        !tab.classes().includes('min-w-0'))
      .map((tab) =>
        tab.text());
    const labelsThatCannotShrink = tabs
      .filter((tab) =>
        !tab.find('span').classes().includes('min-w-0'))
      .map((tab) =>
        tab.text());

    const cutLabels = tabs
      .filter((tab) =>
        tab.find('span').classes().includes('truncate'))
      .map((tab) =>
        tab.text());

    expect(cutLabels).toEqual([]);
    expect(tabsThatCannotShrink).toEqual([]);
    expect(labelsThatCannotShrink).toEqual([]);
  });
});
