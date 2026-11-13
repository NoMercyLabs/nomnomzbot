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

import { mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';

import words from '../i18n/en.json';
import NotFoundView from '../NotFoundView.vue';

async function open() {
  i18n.global.mergeLocaleMessage('en', words);

  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/:pathMatch(.*)*', component: NotFoundView },
    ],
  });

  await router.push('/no-such-page');

  return mount(NotFoundView, { global: { plugins: [i18n, router] } });
}

describe('the 404 page', () => {
  it('has one h1, the task title, and the brand is a link home', async () => {
    const wrapper = await open();

    expect(wrapper.findAll('h1')).toHaveLength(1);
    expect(wrapper.find('h1').text()).toBe(words.site.notFound.title);
    expect(wrapper.find('[data-slot="task-brand"]').attributes('href')).toBe('/');
  });

  it('offers one home button as its primary action', async () => {
    const wrapper = await open();
    const links = wrapper.findAll('[data-slot="task-actions"] a');

    expect(links).toHaveLength(1);
    expect(links[0]!.attributes('href')).toBe('/');
    expect(links[0]!.text()).toBe(words.site.notFound.home);
  });
});
