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
import { defineComponent } from 'vue';
import { createMemoryHistory, createRouter, RouterView, type Router } from 'vue-router';

import { i18n } from '@/i18n';
import { siteRoutes } from '@/router/site';

import words from '../i18n/en.json';
import { featureGroups, sampleChannel, sampleRows } from '../sampleChannel';

const home = words.site.home;
const Shell = defineComponent({ components: { RouterView }, template: '<RouterView />' });

async function openRoot(): Promise<VueWrapper> {
  const router: Router = createRouter({ history: createMemoryHistory(), routes: [siteRoutes] });

  await router.push('/');
  const wrapper: VueWrapper = mount(Shell, { global: { plugins: [router, i18n] } });

  await flushPromises();

  return wrapper;
}

let fetched: ReturnType<typeof vi.fn>;

beforeEach(() => {
  fetched = vi.fn<(input: unknown) => Promise<Response>>(async () => Response.json({}));
  vi.stubGlobal('fetch', fetched);
});

afterEach(() => {
  vi.unstubAllGlobals();
  document.body.innerHTML = '';
});

describe('the feature groups', () => {
  it('shows eight groups, each with the title and line from the home words', async () => {
    const wrapper = await openRoot();
    const cards = wrapper.findAll('[data-feature-group]');

    expect(featureGroups).toHaveLength(8);
    expect(cards).toHaveLength(8);

    featureGroups.forEach((group, index) => {
      const card = cards[index]!;

      expect(card.attributes('data-feature-group')).toBe(group);
      expect(card.find('h3').text()).toBe(home.features[group].title);
      expect(card.find('p').text()).toBe(home.features[group].line);
    });
  });
});

describe('the previews', () => {
  it('render the real dashboard stat tiles with the sample numbers', async () => {
    const wrapper = await openRoot();
    const tiles = wrapper.findAll('[data-slot="stat-tile"]');
    const shown = tiles.map((tile) => tile.text());

    expect(tiles.length).toBeGreaterThanOrEqual(4);

    expect(shown.some((text) => text.includes(sampleChannel.viewers.toLocaleString('en-US')))).toBe(
      true,
    );

    expect(
      shown.some((text) => text.includes(sampleChannel.followers.toLocaleString('en-US'))),
    ).toBe(true);
  });

  it('give every sample row its title from the words and its who from the sample file', async () => {
    const wrapper = await openRoot();
    const moderation = wrapper.find('[data-feature-group="moderation"]');
    const first = sampleRows.moderation[0]!;

    expect(moderation.findAll('[data-slot="summary-row-title"]').length).toBe(
      sampleRows.moderation.length,
    );

    expect(moderation.text()).toContain(first.who);
    expect(moderation.find('[data-slot="status-badge"]').exists()).toBe(true);
  });

  it('say plainly that the channel and the viewers are invented sample data', async () => {
    const wrapper = await openRoot();

    expect(wrapper.text()).toContain(home.features.sampleNote);
    expect(wrapper.text()).toContain(home.demo.sample);
  });
});

describe('the sign-in buttons', () => {
  it('are disabled and say Coming soon, with a note, and none links to the login page', async () => {
    const wrapper = await openRoot();
    const soon = wrapper.findAll('button').filter((button) => button.text() === 'Coming soon');

    expect(home.comingSoon).toBe('Coming soon');
    expect(soon.length).toBeGreaterThanOrEqual(4);

    for (const button of soon) {
      expect(button.attributes('disabled')).toBeDefined();
    }

    expect(wrapper.text()).toContain(home.comingSoonNote);

    expect(wrapper.findAll('a').some((link) => link.attributes('href')?.includes('login'))).toBe(
      false,
    );

    expect(wrapper.text()).not.toContain('Connect your channel');
  });
});

describe('the network', () => {
  it('makes no request at all while the page opens, so nothing needs a session', async () => {
    await openRoot();

    expect(fetched).not.toHaveBeenCalled();
  });
});
