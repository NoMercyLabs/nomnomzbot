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
import { h } from 'vue';

import { laptopWidth, phoneWidth, setWidth } from '@/__tests__/viewport';
import { i18n } from '@/i18n';

import AdminCollectionPage from '../components/AdminCollectionPage.vue';
import en from '../i18n/en.json';

let mounted: VueWrapper | null = null;

beforeEach(() => {
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

async function open(width: number, count: string | null = '42 entries'): Promise<VueWrapper> {
  // The frame's sticky footer, where a phone-width page moves its pager.
  document.body.insertAdjacentHTML('beforeend', '<div data-slot="frame-action-footer"></div>');
  setWidth(width);

  mounted = mount(AdminCollectionPage, {
    attachTo: document.body,
    props: {
      title: 'Audit log',
      count: count ?? undefined,
      page: 2,
      pages: 5,
      state: 'ready',
      listTitle: 'Entries',
    },
    slots: {
      filters: () => h('div', { 'data-test': 'filters' }, 'Filters'),
      notice: () => h('p', { 'data-test': 'notice' }, 'Restricted'),
      default: () => h('table', { 'data-test': 'table' }),
      secondary: () => h('div', { 'data-test': 'secondary' }, 'Below'),
    },
    global: { plugins: [i18n] },
  });

  await flushPromises();

  return mounted;
}

describe('the admin collection page', () => {
  it('shows the result count in the header and marks the dense page body', async () => {
    const wrapper = await open(laptopWidth);

    expect(wrapper.find('header').text()).toContain('42 entries');
    expect(wrapper.find('[data-page-body]').attributes('data-slot')).toBe('page-body');
    expect(wrapper.find('[data-page-body]').classes()).toContain('gap-3');
    expect(wrapper.find('[data-page-body]').classes()).not.toContain('overflow-y-auto');
  });

  it('shows no count before the first answer', async () => {
    const wrapper = await open(laptopWidth, null);

    expect(wrapper.find('header').text()).not.toContain('entries');
  });

  it('puts filters and notice above the lead panel and the secondary slot below it', async () => {
    const wrapper = await open(laptopWidth);
    const order = [...wrapper.find('[data-page-body]').element.children].map((node) => node.getAttribute('data-test') ?? node.getAttribute('data-slot'));

    expect(order.slice(0, 4)).toEqual(['filters', 'notice', 'panel-card', 'secondary']);
    expect(wrapper.find('[data-slot=panel-card] [data-test=table]').exists()).toBe(true);
  });

  it('puts the pager in the panel footer from the tablet width up and emits next and previous', async () => {
    const wrapper = await open(laptopWidth);
    const footer = wrapper.find('[data-slot=panel-card] footer');

    expect(footer.text()).toContain('2');
    expect(document.querySelector('[data-slot=frame-action-footer]')!.textContent).toBe('');

    const buttons = footer.findAll('button');

    await buttons[1]!.trigger('click');
    await buttons[0]!.trigger('click');

    expect(wrapper.emitted('next')).toHaveLength(1);
    expect(wrapper.emitted('previous')).toHaveLength(1);
  });

  it('moves the pager into the sticky page footer on a phone and emits from there', async () => {
    const wrapper = await open(phoneWidth);

    expect(wrapper.find('[data-slot=panel-card] footer').exists()).toBe(false);

    const frameFooter = document.querySelector('[data-slot=frame-action-footer]')!;
    const buttons = frameFooter.querySelectorAll('button');

    expect(buttons).toHaveLength(2);

    buttons[1]!.click();
    buttons[0]!.click();
    await flushPromises();

    expect(wrapper.emitted('next')).toHaveLength(1);
    expect(wrapper.emitted('previous')).toHaveLength(1);
  });

  it('puts a search in the lead panel head, a bar in the header and a detail panel beside the list', async () => {
    document.body.insertAdjacentHTML('beforeend', '<div data-slot="frame-action-footer"></div>');
    setWidth(laptopWidth);

    mounted = mount(AdminCollectionPage, {
      attachTo: document.body,
      props: {
        title: 'Tenants',
        page: 1,
        pages: 1,
        state: 'ready',
        listTitle: 'People',
        searchPlaceholder: 'Find a tenant',
      },
      slots: {
        bar: () => h('div', { 'data-test': 'bar' }, 'Bar'),
        panel: () => h('aside', { 'data-test': 'detail' }, 'Detail'),
        default: () => h('table'),
      },
      global: { plugins: [i18n] },
    });

    await flushPromises();

    expect(mounted.find('header [data-test=bar]').exists()).toBe(true);
    expect(mounted.find('[data-slot=panel-beside] [data-test=detail]').exists()).toBe(true);

    const field = mounted.find('[data-slot=panel-card] input');

    expect(field.attributes('placeholder')).toBe('Find a tenant');

    await field.setValue('kit');

    expect(mounted.emitted('update:search')).toEqual([['kit']]);
  });

  it('shows no search field in the lead panel head without a placeholder', async () => {
    const wrapper = await open(laptopWidth);

    expect(wrapper.find('[data-slot=panel-card] input').exists()).toBe(false);
  });

  it('emits retry from the error state', async () => {
    document.body.insertAdjacentHTML('beforeend', '<div data-slot="frame-action-footer"></div>');
    setWidth(laptopWidth);

    mounted = mount(AdminCollectionPage, {
      attachTo: document.body,
      props: {
        title: 'Audit log',
        page: 1,
        pages: 1,
        state: 'error',
        listTitle: 'Entries',
        errorText: 'Could not load',
      },
      global: { plugins: [i18n] },
    });

    await flushPromises();

    const retry = mounted.findAll('button').find((button) => /retry|again/i.test(button.text()));

    await retry!.trigger('click');

    expect(mounted.emitted('retry')).toHaveLength(1);
  });
});
