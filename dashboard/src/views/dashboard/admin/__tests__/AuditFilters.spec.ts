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

import { QueryClient, VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

type Asked = { path: string; query: Record<string, unknown> };

const asked = vi.hoisted<Asked[]>(() => []);

vi.mock('@/api/client', () => ({
  client: {
    use: () => undefined,
    GET: async (path: string, options?: { params?: { query?: Record<string, unknown> } }) => {
      asked.push({ path, query: { ...options?.params?.query } });

      return { data: { data: [], total: 0 }, response: { status: 200, ok: true } };
    },
  },
}));

import { i18n } from '@/i18n';

import AuditFilters, { type AuditFilterValues } from '../components/AuditFilters.vue';
import en from '../i18n/en.json';

const empty: AuditFilterValues = {
  permission: '',
  outcome: '',
  principalId: '',
  targetBroadcasterId: '',
  from: '',
  to: '',
};

let mounted: VueWrapper | null = null;

function open(values: AuditFilterValues): VueWrapper {
  mounted = mount(AuditFilters, {
    attachTo: document.body,
    props: { modelValue: values },
    global: { plugins: [i18n, [VueQueryPlugin, { queryClient: new QueryClient() }]] },
  });

  return mounted;
}

beforeEach(() => {
  asked.length = 0;
  i18n.global.mergeLocaleMessage('en', en);
});

afterEach(() => {
  mounted?.unmount();
  mounted = null;
  document.body.innerHTML = '';
});

describe('the audit filter pickers', () => {
  it('read the tenants list: the operator picker takes any person, the channel picker only streamers', async () => {
    open(empty);
    await flushPromises();

    const lists = asked.filter((call) => call.path.endsWith('/admin/tenants'));
    const kinds = lists.map((call) => call.query.kind).sort();

    expect(asked.some((call) => call.path.endsWith('/admin/users'))).toBe(false);
    expect(lists).toHaveLength(2);
    expect(kinds).toEqual(['streamer', undefined]);
  });
});

describe('the audit filters on a phone', () => {
  it('folds the six fields behind one Filters toggle that shows no badge while nothing is set', () => {
    const wrapper = open(empty);
    const toggle = wrapper.find('[data-filters-toggle]');

    expect(toggle.exists()).toBe(true);
    expect(toggle.classes()).toContain('md:hidden');
    expect(toggle.text()).toContain('Filters');
    expect(wrapper.find('[data-filters-count]').exists()).toBe(false);
  });

  it('counts the filters that are set in the badge', () => {
    const wrapper = open({
      ...empty,
      permission: 'tenant:suspend',
      outcome: 'Denied',
    });

    expect(wrapper.find('[data-filters-count]').text()).toBe('2');
  });

  it('keeps the fields open at md and up and closed below md until the toggle is pressed', async () => {
    const wrapper = open(empty);
    const fields = wrapper.find('[data-audit-filters]');

    expect(fields.attributes('data-state')).toBe('closed');
    expect(fields.classes()).toContain('max-md:data-[state=closed]:hidden');

    await wrapper.find('[data-filters-toggle]').trigger('click');

    expect(wrapper.find('[data-audit-filters]').attributes('data-state')).toBe('open');
    expect(wrapper.findAll('input')).toHaveLength(5);
  });
});
