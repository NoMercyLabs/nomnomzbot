// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { mount } from '@vue/test-utils';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import { client } from '../client';
import { queryClient } from '../queryClient';
import { useViewAs, viewAs, viewAsHeader, type ViewAsControl } from '../viewAs';

const rechecked = vi.hoisted(() => vi.fn<(router: Router) => Promise<void>>());

vi.mock('@/router/recheckPage', () => ({ recheckOpenPage: rechecked }));

// The call goes through the real middleware chain; fetch is the only fake.
function answer(): ReturnType<typeof vi.fn<typeof fetch>> {
  const fetcher = vi.fn<typeof fetch>(async () => new Response('{}', { status: 200 }));

  return fetcher;
}

afterEach(() => {
  viewAs.value = '';
  localStorage.clear();
});

describe('view as', () => {
  it('sends the previewed role on every call, and nothing once the owner exits', async () => {
    const fetcher = answer();
    const control = useViewAs();

    await control.start('viewer');
    await client.GET('/api/v{version}/channels', { params: { path: { version: '1' } }, fetch: fetcher });
    const withRole = fetcher.mock.calls[0]?.[0] as Request;

    await control.exit();
    await client.GET('/api/v{version}/channels', { params: { path: { version: '1' } }, fetch: fetcher });
    const withoutRole = fetcher.mock.calls[1]?.[0] as Request;

    expect(withRole.headers.get(viewAsHeader)).toBe('viewer');
    expect(withoutRole.headers.has(viewAsHeader)).toBe(false);
  });

  it('keeps the role under nnz.viewAs and drops every cached answer when it changes', async () => {
    const dropped = vi.spyOn(queryClient, 'invalidateQueries');
    const control = useViewAs();

    await control.start('moderator');

    expect(localStorage.getItem('nnz.viewAs')).toBe('moderator');
    expect(dropped).toHaveBeenCalledTimes(1);

    await control.exit();

    expect(viewAs.value).toBe('');
    expect(dropped).toHaveBeenCalledTimes(2);
  });

  it('checks the open page again, after the new role has reached every answer', async () => {
    const order: string[] = [];

    vi.spyOn(queryClient, 'invalidateQueries').mockImplementation(async () => {
      order.push('answers dropped');
    });

    rechecked.mockImplementation(async () => {
      order.push(`page checked as ${viewAs.value || 'owner'}`);
    });

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/', component: defineComponent({ render: () => h('div') }) }],
    });
    let control: ViewAsControl | undefined;

    mount(
      defineComponent({
        setup() {
          control = useViewAs();

          return () => h('div');
        },
      }),
      { global: { plugins: [router] } },
    );

    await control?.start('viewer');
    await control?.exit();

    expect(order).toEqual([
      'answers dropped',
      'page checked as viewer',
      'answers dropped',
      'page checked as owner',
    ]);

    expect(rechecked).toHaveBeenCalledWith(router);
  });
});
