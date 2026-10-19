// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------
import { flushPromises, mount } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import { createMemoryHistory, createRouter } from 'vue-router';

import PageHeader from '../PageHeader.vue';

describe('PageHeader actions group', () => {
  const wrapper = mount(PageHeader, {
    props: { title: 'Music' },
    slots: { actions: '<button>Go</button>' },
  });
  const group = wrapper.find('[data-slot="page-actions"]');
  const classes = group.classes();

  it('is only pushed to the right edge once the header is wide enough for one row', () => {
    expect(classes).toContain('@2xl:ml-auto');
    expect(classes).not.toContain('ml-auto');
  });

  it('fills its own row from the left when the header wraps on a phone', () => {
    expect(classes).toContain('w-full');
    expect(classes).toContain('flex-wrap');
    expect(classes).toContain('@2xl:w-auto');
  });
});

describe('PageHeader without page actions', () => {
  const wrapper = mount(PageHeader, { props: { title: 'Readability' } });
  const classes = wrapper.get('[data-slot="page-actions"]').classes();

  it('keeps the frame icons beside the title on a phone instead of a row of their own', () => {
    expect(classes).not.toContain('w-full');
    expect(classes).toContain('ml-auto');
    expect(classes).toContain('flex-nowrap');
  });
});

describe('PageHeader back link', () => {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      {
        path: '/list',
        name: 'list',
        component: { template: '<div />' },
      },
    ],
  });

  function open(props: Record<string, unknown>) {
    return mount(PageHeader, {
      props: {
        title: 'Pipeline',
        crumb: 'Pipelines',
        ...props,
      },
      slots: { actions: '<button>Go</button>' },
      global: { plugins: [router], mocks: { $t: (key: string): string => key } },
    });
  }

  it('renders before the crumb and the title, not in the actions', () => {
    const header = open({ back: { name: 'list' } });
    const back = header.get('[data-slot="back-link"]').element;
    const title = header.get('h1').element;
    const crumb = header.get('[data-slot="page-crumb"]').element;
    const actions = header.get('[data-slot="page-actions"]').element;

    expect(back.compareDocumentPosition(title) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(back.compareDocumentPosition(crumb) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(actions.contains(back)).toBe(false);
  });

  it('goes to the route location it is given', async () => {
    await router.push('/');
    const header = open({ back: { name: 'list' } });

    await header.get('[data-slot="back-link"]').trigger('click');
    await flushPromises();

    expect(router.currentRoute.value.name).toBe('list');
  });

  it('is absent when the page names no way back', () => {
    expect(open({}).find('[data-slot="back-link"]').exists()).toBe(false);
  });
});
