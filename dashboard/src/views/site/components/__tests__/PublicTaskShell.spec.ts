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

import { mount, type VueWrapper } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import { createMemoryHistory, createRouter } from 'vue-router';

import { i18n } from '@/i18n';

import PublicTaskShell from '../PublicTaskShell.vue';
import TaskActions from '../TaskActions.vue';

async function mountShell(
  slots: Record<string, string>,
  props: { width?: 'compact' | 'wizard'; align?: 'center' | 'start' } = {},
): Promise<VueWrapper> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/login', component: { template: '<div />' } },
    ],
  });

  await router.push('/login');

  return mount(PublicTaskShell, {
    props: { brand: 'NomNomzBot', ...props },
    slots,
    global: { plugins: [i18n, router] },
  });
}

describe('the public task shell', () => {
  it('shows the brand as a link home, not as a heading', async () => {
    const wrapper = await mountShell({ default: '<h1>Sign in</h1>' });
    const brand = wrapper.find('[data-slot="task-brand"]');

    expect(brand.element.tagName).toBe('A');
    expect(brand.attributes('href')).toBe('/');
    expect(brand.text()).toBe('NomNomzBot');
    expect(brand.find('h1, h2, h3').exists()).toBe(false);
    expect(wrapper.findAll('h1')).toHaveLength(1);
    expect(wrapper.find('h1').text()).toBe('Sign in');
  });

  it('draws the task title larger than the brand link', async () => {
    const wrapper = await mountShell({ default: '<h1>Sign in</h1>' });
    const column = wrapper.get('[data-slot="task-column"]');

    expect(wrapper.get('[data-slot="task-brand"]').classes()).toContain('text-base');
    expect(column.classes()).toContain('[&_h1]:text-xl');
  });

  it('puts the card in the max-w-100 column and the below slot under it', async () => {
    const wrapper = await mountShell({
      default: '<section id="card">Card</section>',
      below: '<p id="terms">Terms</p>',
    });
    const column = wrapper.find('[data-slot="task-column"]');

    expect(column.classes()).toContain('max-w-100');
    expect(column.find('#card').exists()).toBe(true);

    const order = column.findAll('#card, #terms').map((node) => node.attributes('id'));

    expect(order).toEqual(['card', 'terms']);
  });

  it('widens the column to max-w-xl for a wizard and keeps the compact width otherwise', async () => {
    const wizard = await mountShell({ default: '<section>Card</section>' }, { width: 'wizard' });
    const column = wizard.get('[data-slot="task-column"]');

    expect(column.classes()).toContain('max-w-xl');
    expect(column.classes()).not.toContain('max-w-100');
  });

  it('centers the column by default and pins it to the top when asked', async () => {
    const centered = await mountShell({ default: '<section>Card</section>' });
    const pinned = await mountShell({ default: '<section>Card</section>' }, { align: 'start' });

    expect(centered.get('main').classes()).toContain('justify-center');
    expect(pinned.get('main').classes()).toContain('justify-start');
    expect(pinned.get('main').classes()).not.toContain('justify-center');
  });

  it('renders no below wrapper when the page gives no small print', async () => {
    const wrapper = await mountShell({ default: '<section>Card</section>' });

    expect(wrapper.find('[data-slot="task-below"]').exists()).toBe(false);
  });

  it('renders the language picker', async () => {
    const wrapper = await mountShell({ default: '<section>Card</section>' });

    expect(wrapper.find(`[aria-label="${i18n.global.t('common.language')}"]`).exists()).toBe(true);
  });
});

describe('the task actions stack', () => {
  it('stacks its buttons in one column and stretches each to the full width', () => {
    const wrapper = mount(TaskActions, {
      slots: { default: '<button>One</button><button>Two</button>' },
    });

    expect(wrapper.classes()).toEqual(expect.arrayContaining(['flex', 'flex-col', '*:w-full']));
    expect(wrapper.findAll('button')).toHaveLength(2);
  });
});
