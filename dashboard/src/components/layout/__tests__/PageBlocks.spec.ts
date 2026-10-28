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
import { afterEach, describe, expect, it } from 'vitest';
import { defineComponent, h } from 'vue';

import { laptopWidth, phoneWidth, setWidth } from '@/__tests__/viewport';
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
  const panels = [{ id: 'one', label: 'One' }, { id: 'two', label: 'Two' }];
  const realMatchMedia = window.matchMedia;

  afterEach(() => {
    window.matchMedia = realMatchMedia;
  });

  it('splits the height into the rows it is given and places a cell by span and rows', () => {
    setWidth(laptopWidth);

    const grid: VueWrapper = mount(BoardGrid, { props: { panels, rows: 3 } });
    const cell: VueWrapper = mount(BoardCell, { props: {
      panel: 'one',
      span: 5,
      rows: 2,
    } });

    const board = grid.find('[data-slot="board-grid"]');

    expect(board.attributes('data-board-wide')).toBe('true');
    expect(board.attributes('style')).toContain('--board-rows: repeat(3, minmax(0, 1fr))');
    expect(cell.attributes('style')).toContain('--cell-span: span 5');
    expect(cell.attributes('style')).toContain('--cell-rows: span 2');
  });

  it('gives a cell the full width and one row by default', () => {
    const cell = mount(BoardCell, { props: { panel: 'one' } });

    expect(cell.attributes('style')).toContain('--cell-span: span 12');
    expect(cell.attributes('style')).toContain('--cell-rows: span 1');
  });

  // Stoney 2026-10-14, on his phone: "Nice mobile view...". The 12 columns squeezed three panels into a phone
  // width, one letter per line. Below laptop width a board of one panel stacks in one column; a board of more
  // panels shows them one at a time behind tabs (BoardGrid.spec).
  it('stacks a single panel in one column below laptop width', () => {
    setWidth(phoneWidth);

    const grid: VueWrapper = mount(BoardGrid, { props: { panels: panels.slice(0, 1), rows: 2 } });
    const cell: VueWrapper = mount(BoardCell, { props: {
      panel: 'one',
      span: 3,
      rows: 2,
    } });

    const board = grid.find('[data-slot="board-grid"]');

    expect(board.attributes('data-board-wide')).toBeUndefined();
    expect(board.classes()).toEqual(expect.arrayContaining(['grid-cols-1', 'overflow-y-auto']));
    expect(board.classes()).not.toContain('grid-cols-12');
    expect(board.attributes('style')).not.toContain('grid-template-rows');
    expect(cell.attributes('style')).not.toMatch(/grid-(column|row)/);

    expect(cell.classes()).toEqual(expect.arrayContaining([
      'in-data-board-wide:col-(--cell-span)',
      'in-data-board-wide:row-(--cell-rows)',
    ]));
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

  describe('the aside', () => {
    const realMatchMedia = window.matchMedia;
    let mounted: VueWrapper | undefined;

    async function withAside(width: number, asideOpen = false): Promise<VueWrapper> {
      setWidth(width);

      mounted = mount(ListDetail, {
        attachTo: document.body,
        props: { asideLabel: 'Runs', asideOpen },
        slots: { ...slots, aside: '<p>aside</p>' },
      });

      await flushPromises();

      return mounted;
    }

    afterEach(() => {
      mounted?.unmount();
      mounted = undefined;
      window.matchMedia = realMatchMedia;
      document.body.innerHTML = '';
    });

    it('adds the aside column only when an aside is given, always shown where it fits', async () => {
      setWidth(1440);
      expect(mount(ListDetail, { slots }).find('[data-slot="list-detail-aside"]').exists()).toBe(false);

      const wrapper = await withAside(1440);

      expect(wrapper.find('aside[data-slot="list-detail-aside"]').text()).toBe('aside');
      expect(document.body.querySelector('[role="dialog"]')).toBeNull();
    });

    // Stoney 2026-10-14: "Make sure you overlay what no longer fits with a drawer from left or right". The aside
    // vanished below laptop width with no way to reach it on most pages.
    it('turns the aside into a drawer from the right where it no longer fits, opened on demand', async () => {
      const closed = await withAside(1024);

      expect(closed.find('aside').exists()).toBe(false);
      expect(document.body.textContent).not.toContain('aside');

      closed.unmount();
      document.body.innerHTML = '';

      await withAside(1024, true);

      const drawer = document.body.querySelector<HTMLElement>('[role="dialog"][data-slot="list-detail-aside"]');

      expect(drawer).not.toBeNull();
      expect(drawer!.dataset.side).toBe('right');
      expect(drawer!.textContent).toContain('aside');
    });
  });
});

describe('the page header', () => {
  it('shows crumb, title and the slots, and a control bar only with bar content', () => {
    const plain: VueWrapper = mount(PageHeader, { props: { title: 'Stream' } });
    const full: VueWrapper = mount(PageHeader, {
      props: { title: 'Stream', crumb: 'Channel' },
      slots: {
        status: '<i>live</i>',
        actions: '<b>Go</b>',
        meta: 'Updated now',
        bar: '<u>controls</u>',
      },
    });

    expect(plain.find('h1').text()).toBe('Stream');
    expect(plain.find('[data-slot="page-crumb"]').exists()).toBe(false);
    expect(plain.find('[data-slot="page-bar"]').exists()).toBe(false);
    expect(full.find('[data-slot="page-bar"] u').text()).toBe('controls');
    expect(full.find('[data-slot="page-crumb"]').text()).toBe('Channel');

    expect(full.text()).toContain('Updated now');

    ['i', 'b', 'u'].forEach((tag) => expect(full.find(tag).exists()).toBe(true));
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
    expect(wrapper.classes().some((name) => name.startsWith('bg-'))).toBe(false);
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

  // A narrow drawer has no room for a box and its padding: the plain stat is the mockup's bare
  // .stat, so "92.8 hours watched" stays on one line in a 320px drawer.
  it('drops the box and its padding when plain, and sets the context in extra-small text', () => {
    const tile: VueWrapper = mountTile({
      label: 'Messages',
      value: 2536,
      context: '92.8 hours watched',
    });
    const plain: VueWrapper = mountTile({
      label: 'Messages',
      value: 2536,
      context: '92.8 hours watched',
      plain: true,
    });
    const classes = (wrapper: VueWrapper): string[] => wrapper.find('[data-slot="stat-tile"]').classes();

    expect(classes(tile)).toEqual(expect.arrayContaining(['border', 'p-4']));
    expect(classes(plain)).not.toEqual(expect.arrayContaining(['border']));
    expect(classes(plain)).not.toContain('p-4');
    expect(plain.find('[data-slot="stat-context"]').classes()).toContain('text-xs');
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
