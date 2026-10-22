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
import { createMemoryHistory, createRouter, type Router } from 'vue-router';
import { defineComponent, h } from 'vue';
import { RouterView } from 'vue-router';

import { i18n } from '@/i18n';
import { isSignInOpen } from '@/lib/flags';
import { siteRoutes } from '@/router/site';

import words from '../i18n/en.json';
import { areaPages, groupPages, previewAreas, type PreviewArea } from '../preview/previewAreas';

// The real dashboard behind a preview is proved in previews.spec.ts. Here the frame is a plain box that keeps
// the path it was given, so the page around it can be tested fast.
vi.mock('../preview/PreviewFrame.vue', () => ({
  __esModule: true,
  default: defineComponent({
    props: { path: { type: String, required: true } },
    setup: (props) => () => h('div', { 'data-testid': 'preview-frame', 'data-path': props.path }),
  }),
}));

const Shell = defineComponent({ components: { RouterView }, template: '<RouterView />' });

async function openRoot(): Promise<{ wrapper: VueWrapper; router: Router }> {
  const router: Router = createRouter({ history: createMemoryHistory(), routes: [siteRoutes] });

  await router.push('/');
  const wrapper: VueWrapper = mount(Shell, { global: { plugins: [router, i18n] } });

  await flushPromises();

  return { wrapper, router };
}

afterEach(() => {
  document.body.innerHTML = '';
});

describe('the site root', () => {
  it('shows the home page instead of redirecting to sign in', async () => {
    const { wrapper, router } = await openRoot();

    expect(router.currentRoute.value.name).toBe('home');
    expect(wrapper.find('h1').text()).toBe('Run your stream. Skip the busywork.');
  });

  it('never sends a visitor to the app while sign-in is closed', async () => {
    const { wrapper } = await openRoot();

    expect(wrapper.findAll('a').some((link) => link.attributes('href') === '/app')).toBe(false);
  });

  // Stoney 2026-10-13, on a phone (translated from Dutch): "it is not really user-friendly on mobile, is it."
  it('puts the links, the language and connect behind one menu button on a small screen', async () => {
    const { wrapper } = await openRoot();

    await wrapper.find('button[aria-label="Open menu"]').trigger('click');
    await flushPromises();

    const panel: HTMLElement | null = document.querySelector('[role="dialog"]');

    expect(panel).not.toBeNull();
    const links: string[] = [...panel!.querySelectorAll('a')].map((link) =>
      link.textContent!.trim(),
    );

    expect(links).toEqual([
      'Features',
      'Self-host vs hosted',
      'Integrations',
      'GitHub',
    ]);

    expect(panel!.querySelector('[data-slot="select-trigger"]')).not.toBeNull();
    expect(panel!.querySelector('button[disabled]')!.textContent).toBe('Coming soon');
  });

  it('keeps the license restriction next to the hosting choice', async () => {
    const { wrapper } = await openRoot();

    expect(wrapper.text()).toContain(
      'Only NoMercy Labs may operate NomNomzBot as a paid service for other streamers',
    );

    expect(wrapper.text()).toContain('Running your own bot stays free and unrestricted, forever');
  });

  it('switches the dashboard preview in the hero when another tab is chosen', async () => {
    const { wrapper } = await openRoot();
    const hero = wrapper.find('[data-testid="hero-shot"]');
    const tabs = hero.findAll('[role="tab"]');
    const shownPath = (): string | undefined =>
      hero.find('[data-testid="preview-frame"]').attributes('data-path');

    expect(tabs).toHaveLength(previewAreas.length);
    expect(tabs[0]!.attributes('aria-selected')).toBe('true');
    expect(shownPath()).toBe(areaPages.pipelines.path);

    const moderation = tabs.find((tab) => tab.text() === 'Moderation')!;

    await moderation.trigger('mousedown');
    await flushPromises();

    expect(moderation.attributes('aria-selected')).toBe('true');
    expect(tabs[0]!.attributes('aria-selected')).toBe('false');
    expect(shownPath()).toBe(areaPages.moderation.path);
    expect(hero.text()).toContain(words.site.home.areas.moderation.caption);
  });
});

describe('the network', () => {
  it('makes no request at all while the page opens, so nothing needs a session', async () => {
    const fetched = vi.fn<(input: unknown) => Promise<Response>>(async () => Response.json({}));

    vi.stubGlobal('fetch', fetched);
    await openRoot();
    vi.unstubAllGlobals();

    expect(fetched).not.toHaveBeenCalled();
  });
});

describe('the feature groups', () => {
  it('give each area a title, a text, one outline link to the page source and a wide preview', async () => {
    const { wrapper } = await openRoot();
    const groups = wrapper.findAll('[data-feature-group]');

    expect(groups.map((group) => group.attributes('data-feature-group'))).toEqual([
      ...previewAreas,
    ]);

    for (const group of groups) {
      const area = group.attributes('data-feature-group') as PreviewArea;
      const copy = words.site.home.areas[area];
      const actions = group.findAll('a, button');

      expect(group.find('h3').text()).toBe(copy.title);
      expect(group.text()).toContain(copy.text);
      expect(group.text()).toContain(copy.caption);
      expect(actions).toHaveLength(1);

      expect(actions[0]!.attributes('href')).toBe(
        `${words.site.home.githubUrl}/tree/master/dashboard/src/views/dashboard/${groupPages[area].source}`,
      );

      expect(group.find('[data-testid="lazy-preview"]').exists()).toBe(true);
    }
  });

  it('say once, above the groups, that the channel and the viewers are invented', async () => {
    const { wrapper } = await openRoot();

    expect(wrapper.text()).toContain(words.site.home.features.sampleNote);
    expect(wrapper.text()).toContain('crumb_tv');
  });
});

describe.each([false, true])('with sign-in open = %s', (open) => {
  beforeEach(() => {
    vi.stubEnv('VITE_SIGN_IN_OPEN', String(open));
  });

  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it('renders the same page: the hero and the previews', async () => {
    const { wrapper } = await openRoot();

    expect(isSignInOpen()).toBe(open);
    expect(wrapper.find('h1').text()).toBe(words.site.home.hero.title);
    expect(wrapper.find('[data-testid="hero-shot"]').exists()).toBe(true);
    expect(wrapper.findAll('[data-feature-group]')).toHaveLength(previewAreas.length);
  });

  it('shows a link to the sign-in page when sign-in is open and a disabled button when it is closed', async () => {
    const { wrapper } = await openRoot();

    const links = wrapper
      .findAll('a')
      .filter((link) => link.text() === words.site.home.signIn && link.attributes('href') === '/login');
    const closed = wrapper
      .findAll('button[disabled]')
      .filter((button) => button.text() === words.site.home.comingSoon);

    expect(links.length > 0).toBe(open);
    expect(closed.length > 0).toBe(!open);
  });
});
