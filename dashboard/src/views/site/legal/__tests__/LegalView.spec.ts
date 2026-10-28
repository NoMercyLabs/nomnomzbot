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
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';
import { siteRoutes } from '@/router/site';

import en from '../i18n/en.json';
import nl from '../i18n/nl.json';
import LegalView from '../LegalView.vue';
import { legalFacts, legalPaths } from '../legalFacts';

let mounted: VueWrapper | null = null;

async function open(doc: 'privacy' | 'terms'): Promise<VueWrapper> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: { template: '<div />' } }, { path: '/:rest(.*)', component: { template: '<div />' } }],
  });

  await router.push('/');

  mounted = mount(LegalView, {
    props: { doc },
    global: { plugins: [i18n, router] },
  });

  await flushPromises();

  return mounted;
}

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
});

// Stoney 2026-10-23: Discord and Twitch ask for a Terms and a Privacy Policy address; the bot needs both.
describe('the legal pages', () => {
  it('serve both documents to a signed-out visitor at the addresses given to Discord', () => {
    const paths: Record<string, unknown> = Object.fromEntries(
      (siteRoutes.children ?? []).map((route) => [route.path, route.meta]),
    );

    expect(legalPaths).toEqual({ privacy: 'privacy-policy', terms: 'terms-of-service' });
    expect(Object.keys(paths)).toEqual(expect.arrayContaining(['privacy-policy', 'terms-of-service']));
    expect(paths['privacy-policy']).toBeUndefined();
    expect(paths['terms-of-service']).toBeUndefined();
  });

  it.each([
    ['privacy', 'Privacy Policy'],
    ['terms', 'Terms of Service'],
  ] as const)('show the %s document as numbered sections with the facts filled in', async (doc, title) => {
    const view = await open(doc);
    const sections = view.findAll('[data-legal-section]');

    expect(view.get('h1').text()).toBe(title);
    expect(sections.length).toBeGreaterThanOrEqual(10);

    expect(sections.map((section) => section.attributes('data-legal-section'))).toEqual(
      sections.map((_, index) => String(index + 1)),
    );

    expect(view.text()).toContain(legalFacts.company);
    expect(view.text()).toContain(legalFacts.email);
    expect(view.text()).not.toMatch(/\{\w+\}/);
  });

  // The original needs no note, and the note names no language, so a new translation never makes it wrong.
  it('show no translation note on the English original', async () => {
    const text = (await open('privacy')).text();

    expect(text).not.toContain('is a translation');
    expect(text).not.toContain('Dutch');
  });

  it('say in the Privacy Policy what the bot keeps, why, and how to erase it', async () => {
    const text = (await open('privacy')).text();

    for (const word of ['Twitch', 'Discord', 'legitimate interest', 'data page', 'erased']) {
      expect(text).toContain(word);
    }
  });

  it.each(['privacy', 'terms'] as const)(
    'have a Dutch %s document with the same sections and the facts filled in',
    async (doc) => {
      const english = (await open(doc)).findAll('[data-legal-section]').length;

      mounted?.unmount();
      i18n.global.mergeLocaleMessage('nl', nl);
      i18n.global.locale.value = 'nl';

      try {
        const view = await open(doc);

        expect(view.findAll('[data-legal-section]')).toHaveLength(english);
        expect(view.text()).toContain(legalFacts.company);
        expect(view.text()).not.toMatch(/\{\w+\}/);
        expect(view.text()).not.toContain('the Netherlands');
        expect(view.text()).toContain('23 okt 2026');
        expect(view.text()).toContain('geldt de Engelse tekst');
      } finally {
        i18n.global.locale.value = 'en';
      }
    },
  );

  it('link to each other', async () => {
    const privacy = await open('privacy');

    expect(privacy.get('footer a').attributes('href')).toBe('/terms-of-service');
    privacy.unmount();

    const terms = await open('terms');

    expect(terms.get('footer a').attributes('href')).toBe('/privacy-policy');
  });
});
