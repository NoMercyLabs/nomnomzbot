// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { mount, type VueWrapper } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';
import { defineComponent, h } from 'vue';

import { frameActionsKey } from '@/components/layout/frameActions';
import { i18n } from '@/i18n';
import BoardCell from '@/components/layout/BoardCell.vue';
import BoardGrid from '@/components/layout/BoardGrid.vue';
import KeyValueList from '@/components/layout/KeyValueList.vue';
import ListDetail from '@/components/layout/ListDetail.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import StatTile from '@/components/layout/StatTile.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';

describe('the board', () => {
  it('splits the height into the rows it is given and places a cell by span and rows', () => {
    const grid: VueWrapper = mount(BoardGrid, { props: { rows: 3 } });
    const cell: VueWrapper = mount(BoardCell, { props: { span: 5, rows: 2 } });

    expect(grid.attributes('style')).toContain('repeat(3, minmax(0, 1fr))');
    expect(cell.attributes('style')).toContain('grid-column: span 5');
    expect(cell.attributes('style')).toContain('grid-row: span 2');
  });

  it('gives a cell the full width and one row by default', () => {
    const cell = mount(BoardCell);

    expect(cell.attributes('style')).toContain('grid-column: span 12');
    expect(cell.attributes('style')).toContain('grid-row: span 1');
  });
});

describe('the list and detail layout', () => {
  const slots = { list: '<p>list</p>', detail: '<p>detail</p>' };

  it('shows the list on a phone until a detail is opened', () => {
    const wrapper: VueWrapper = mount(ListDetail, { slots });

    expect(wrapper.find('[data-slot="list-detail-list"]').classes()).not.toContain('max-md:hidden');
    expect(wrapper.find('[data-slot="list-detail-detail"]').classes()).toContain('max-md:hidden');
  });

  it('shows the detail over the list on a phone once it is open', () => {
    const wrapper: VueWrapper = mount(ListDetail, { props: { showDetail: true }, slots });

    expect(wrapper.find('[data-slot="list-detail-list"]').classes()).toContain('max-md:hidden');

    expect(wrapper.find('[data-slot="list-detail-detail"]').classes()).not.toContain(
      'max-md:hidden',
    );
  });

  it('adds the aside column only when an aside is given', () => {
    expect(mount(ListDetail, { slots }).find('[data-slot="list-detail-aside"]').exists()).toBe(
      false,
    );

    const withAside: VueWrapper = mount(ListDetail, { slots: { ...slots, aside: '<p>aside</p>' } });

    expect(withAside.find('[data-slot="list-detail-aside"]').text()).toBe('aside');
  });
});

describe('the page header', () => {
  it('shows crumb, title and the slots, and a tab row only with tabs', () => {
    const plain: VueWrapper = mount(PageHeader, { props: { title: 'Stream' } });
    const full: VueWrapper = mount(PageHeader, {
      props: { title: 'Stream', crumb: 'Channel' },
      slots: {
        status: '<i>live</i>',
        actions: '<b>Go</b>',
        meta: 'Updated now',
        tabs: '<u>tabs</u>',
      },
    });

    expect(plain.find('h1').text()).toBe('Stream');
    expect(plain.find('[data-slot="page-crumb"]').exists()).toBe(false);
    expect(plain.find('u').exists()).toBe(false);
    expect(full.find('[data-slot="page-crumb"]').text()).toBe('Channel');

    expect(full.text()).toContain('Updated now');

    ['i', 'b', 'u'].forEach((tag: string) => expect(full.find(tag).exists()).toBe(true));
  });

  it('ends its title row with the controls the frame gives it, after the page action', () => {
    const frameControls = defineComponent({ render: () => h('em', 'Shield mode') });
    const wrapper: VueWrapper = mount(PageHeader, {
      props: { title: 'Home' },
      slots: { actions: '<b>Open chat</b>' },
      global: { provide: { [frameActionsKey as symbol]: frameControls } },
    });

    const row: string[] = wrapper
      .find('h1')
      .element.parentElement!.textContent!.split(/(?=Open chat|Shield mode)/);

    expect(row.slice(-2)).toEqual(['Open chat', 'Shield mode']);
  });
});

describe('the status badge', () => {
  it('shows the words and a dot, never a tinted pill', () => {
    const wrapper: VueWrapper = mount(StatusBadge, { props: { tone: 'ok', label: 'Connected' } });

    expect(wrapper.text()).toBe('Connected');
    expect(wrapper.attributes('data-tone')).toBe('ok');
    expect(wrapper.find('[aria-hidden="true"]').classes()).toContain('bg-success');
    expect(wrapper.classes().some((name: string) => name.startsWith('bg-'))).toBe(false);
  });

  it('paints harm red and a quiet state gray', () => {
    const bad: VueWrapper = mount(StatusBadge, { props: { tone: 'bad', label: 'Failed' } });
    const off: VueWrapper = mount(StatusBadge, { props: { tone: 'off', label: 'Off' } });

    expect(bad.find('[aria-hidden="true"]').classes()).toContain('bg-destructive');
    expect(off.find('[aria-hidden="true"]').classes()).toContain('bg-muted-foreground');
  });
});

describe('the stat tile', () => {
  const mountTile = (props: Record<string, unknown>): VueWrapper =>
    mount(StatTile, { props: props as never, global: { plugins: [i18n] } });

  it('shows label, value and context, with no icon', () => {
    const wrapper: VueWrapper = mountTile({
      label: 'Channels',
      value: 26,
      context: '8 set up',
    });

    expect(wrapper.text()).toBe('Channels268 set up');
    expect(wrapper.find('svg').exists()).toBe(false);
  });

  it('groups the digits of a number the way the reader writes them', () => {
    expect(mountTile({ label: 'Messages', value: 54431 }).find('b').text()).toBe('54,431');
    expect(mountTile({ label: 'Uptime', value: '2:14:09' }).find('b').text()).toBe('2:14:09');
  });

  it('colors the value only when it is a warning', () => {
    const calm: VueWrapper = mountTile({ label: 'A', value: 1 });
    const warn: VueWrapper = mountTile({
      label: 'A',
      value: 1,
      warn: true,
    });

    expect(calm.find('b').classes()).not.toContain('text-destructive');
    expect(warn.find('b').classes()).toContain('text-destructive');
  });
});

describe('the key value list', () => {
  it('pairs each label with its value', () => {
    const wrapper: VueWrapper = mount(KeyValueList, {
      props: {
        items: [
          { label: 'Region', value: 'EU' },
          { label: 'Slots', value: 0 },
        ],
      },
    });

    expect(wrapper.findAll('dt').map((node) => node.text())).toEqual(['Region', 'Slots']);
    expect(wrapper.findAll('dd').map((node) => node.text())).toEqual(['EU', '0']);
  });

  it('shows a dash with the reason when a value has no source', () => {
    const wrapper: VueWrapper = mount(KeyValueList, {
      props: {
        items: [
          {
            label: 'Uptime',
            value: null,
            reason: 'Not recorded',
          },
        ],
      },
    });

    expect(wrapper.find('dd').text()).toBe('- Not recorded');
  });
});
