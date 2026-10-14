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
import { describe, expect, it } from 'vitest';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import PageTabs, { type PageTab } from '@/components/layout/PageTabs.vue';

const tabs: PageTab[] = [
  { value: 'info', label: 'Stream info' },
  { value: 'run', label: 'Run the stream' },
  {
    value: 'history',
    label: 'History',
    count: 12,
  },
];

async function open(path: string): Promise<{ router: Router; wrapper: VueWrapper }> {
  const router: Router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: { render: () => null } }],
  });

  await router.push(path);
  const wrapper: VueWrapper = mount(PageTabs, { props: { tabs }, global: { plugins: [router] } });

  await flushPromises();

  return { router, wrapper };
}

function selectedLabel(wrapper: VueWrapper): string | undefined {
  return wrapper
    .findAll('[role="tab"]')
    .find((tab) => tab.attributes('aria-selected') === 'true')
    ?.text();
}

describe('the page tabs', () => {
  it('opens the tab named in the query, so a link lands on it', async () => {
    const { wrapper } = await open('/?view=run');

    expect(selectedLabel(wrapper)).toBe('Run the stream');
  });

  it('falls back to the first tab when the query names an unknown tab', async () => {
    const { wrapper } = await open('/?view=gone');

    expect(selectedLabel(wrapper)).toBe('Stream info');
  });

  it('puts the chosen tab in the query and keeps other query values', async () => {
    const { router, wrapper } = await open('/?q=abc');

    await wrapper.findAll('[role="tab"]')[1]!.trigger('mousedown', { button: 0 });
    await flushPromises();

    expect(router.currentRoute.value.query).toEqual({ q: 'abc', view: 'run' });
    expect(selectedLabel(wrapper)).toBe('Run the stream');
  });

  it('returns to the earlier tab on Back', async () => {
    const { router, wrapper } = await open('/');

    await wrapper.findAll('[role="tab"]')[2]!.trigger('mousedown', { button: 0 });
    await flushPromises();
    expect(router.currentRoute.value.query.view).toBe('history');

    router.back();
    await flushPromises();

    expect(selectedLabel(wrapper)).toBe('Stream info');
  });

  it('shows the count of a tab beside its label', async () => {
    const { wrapper } = await open('/');

    expect(wrapper.findAll('[role="tab"]')[2]!.text()).toBe('History 12');
  });
});
