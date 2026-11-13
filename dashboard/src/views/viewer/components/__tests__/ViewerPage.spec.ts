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

import { i18n } from '@/i18n';

import ViewerPage from '../ViewerPage.vue';

function mountPage(props: Record<string, unknown> = {}) {
  return mount(ViewerPage, {
    props: { title: 'Your stats', ...props },
    slots: {
      default: '<p data-body>Body</p>',
      status: '<span data-status>Status</span>',
      actions: '<button data-action>Action</button>',
    },
    global: { plugins: [i18n] },
  });
}

describe('ViewerPage', () => {
  it('shows the title as the one h1, with the status and the action beside it and the body below', () => {
    const wrapper = mountPage();

    expect(wrapper.findAll('h1')).toHaveLength(1);
    expect(wrapper.get('h1').text()).toBe('Your stats');
    expect(wrapper.get('[data-slot="viewer-page-status"] [data-status]').text()).toBe('Status');
    expect(wrapper.get('[data-slot="viewer-page-actions"] [data-action]').text()).toBe('Action');
    expect(wrapper.get('[data-body]').text()).toBe('Body');
  });

  it('adds no width of its own: the viewer layout owns the max width', () => {
    const wrapper = mountPage();

    expect(wrapper.classes().some((name) => name.startsWith('max-w-'))).toBe(false);
  });

  it('shows a busy skeleton instead of the body while loading', () => {
    const wrapper = mountPage({ state: 'loading' });

    expect(wrapper.find('[data-body]').exists()).toBe(false);
    expect(wrapper.get('[data-slot="viewer-page-loading"]').attributes('aria-busy')).toBe('true');
  });

  it('shows the error line with a retry that asks the page to read again', async () => {
    const wrapper = mountPage({
      state: 'error',
      errorText: 'Not found',
      retryLabel: 'Try again',
    });

    expect(wrapper.find('[data-body]').exists()).toBe(false);
    expect(wrapper.get('[role="alert"]').text()).toContain('Not found');

    await wrapper.get('[role="alert"] button').trigger('click');

    expect(wrapper.emitted('retry')).toHaveLength(1);
  });

  it('shows the empty text instead of the body when nothing is there', () => {
    const wrapper = mountPage({
      state: 'empty',
      emptyText: 'Nothing yet',
    });

    expect(wrapper.find('[data-body]').exists()).toBe(false);
    expect(wrapper.get('[data-slot="panel-empty"]').text()).toBe('Nothing yet');
  });

  it('keeps the header in every state', () => {
    const wrapper = mountPage({ state: 'error' });

    expect(wrapper.get('h1').text()).toBe('Your stats');
    expect(wrapper.find('[data-action]').exists()).toBe(true);
  });
});
