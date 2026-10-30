// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';
import { defineComponent } from 'vue';
import { RouterView } from 'vue-router';

import { i18n } from '@/i18n';
import { isSignInOpen } from '@/lib/flags';
import { siteRoutes } from '@/router/site';

import words from '../i18n/en.json';
import {
  groupPages,
  previewAreas,
  showcaseImage,
  type PreviewArea,
} from '../preview/previewAreas';

const Shell = defineComponent({ components: { RouterView }, template: '<RouterView />' });

function siteRouter(): Router {
  return createRouter({ history: createMemoryHistory(), routes: [siteRoutes] });
}

// The first visit to the root imports the site frame and the home page cold (3 s alone, far more beside a
// build). It happens while the file loads, where no test timeout runs, so the first test measures the page.
await siteRouter().push('/');

async function openRoot(): Promise<{ wrapper: VueWrapper; router: Router }> {
  const router = siteRouter();

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
      'Documentation',
      'GitHub',
    ]);

    const docs: HTMLAnchorElement | undefined = [...panel!.querySelectorAll('a')].find(
      (link) => link.textContent!.trim() === 'Documentation',
    );

    expect(docs?.getAttribute('href')).toBe(words.site.home.docsUrl);
    expect(docs?.getAttribute('href')).toBe('https://docs.nomnomz.bot');

    expect(panel!.querySelector('[data-slot="select-trigger"]')).not.toBeNull();
    expect(panel!.querySelector('button[disabled]')!.textContent).toBe('Coming soon');
  });

  it('keeps the license restriction next to the hosting choice', async () => {
    const { wrapper } = await openRoot();

    expect(wrapper.text()).toContain(
      'You may not earn money with NomNomzBot: no selling it, and no paid hosting or paid service built on it. That right is reserved to NoMercy Labs.',
    );

    expect(wrapper.text()).toContain(
      'Running it for yourself, your friends and your community is free, forever',
    );
  });

  it('switches the picture in the hero when another tab is chosen', async () => {
    const { wrapper } = await openRoot();
    const hero = wrapper.find('[data-testid="hero-shot"]');
    const tabs = hero.findAll('[role="tab"]');

    const shown = () => {
      const image = hero.find('img[data-testid="showcase-image"]');

      return {
        src: image.attributes('src'),
        alt: image.attributes('alt'),
        loading: image.attributes('loading'),
      } as const satisfies {
        src?: string;
        alt?: string;
        loading?: string;
      };
    };

    expect(tabs).toHaveLength(previewAreas.length);
    expect(tabs[0]!.attributes('aria-selected')).toBe('true');
    expect(shown().src).toBe('/showcase/pipelines-hero.webp');
    expect(shown().src).toBe(showcaseImage('pipelines', 'hero'));
    // The picture the visitor sees first is not held back.
    expect(shown().loading).toBe('eager');
    expect(shown().alt).toBe(words.site.home.areas.pipelines.caption);

    const moderation = tabs.find((tab) => tab.text() === 'Moderation')!;

    await moderation.trigger('mousedown');
    await flushPromises();

    expect(moderation.attributes('aria-selected')).toBe('true');
    expect(tabs[0]!.attributes('aria-selected')).toBe('false');
    expect(shown().src).toBe('/showcase/moderation-hero.webp');
    expect(shown().alt).toBe(words.site.home.areas.moderation.caption);
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
  it('give each area a title, a text, one outline link to the page source and a wide picture', async () => {
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

      const image = group.find('img[data-testid="showcase-image"]');

      // 1920x1080 sets the box before the file arrives, and a group below the fold loads when it nears the screen.
      expect(image.attributes('src')).toBe(`/showcase/${area}-group.webp`);
      expect(image.attributes('width')).toBe('1920');
      expect(image.attributes('height')).toBe('1080');
      expect(image.attributes('loading')).toBe('lazy');
      expect(image.attributes('alt')).toBe(copy.caption);
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
