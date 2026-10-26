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
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h } from 'vue';

import RowEditDelete from '@/components/layout/RowEditDelete.vue';

let mounted: VueWrapper | undefined;

beforeEach(() => {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe(): void {}
      unobserve(): void {}
      disconnect(): void {}
    },
  );
});

afterEach(() => {
  vi.unstubAllGlobals();
  mounted?.unmount();
  document.body.innerHTML = '';
});

describe('the row edit and delete actions', () => {
  it('shows Edit, then Delete, as two named 36 px icon buttons', () => {
    mounted = mount(RowEditDelete, {
      props: { editLabel: 'Edit Home server', deleteLabel: 'Delete Home server' },
    });

    const buttons = mounted.findAll('button');

    expect(buttons.map((button) => button.attributes('aria-label'))).toEqual([
      'Edit Home server',
      'Delete Home server',
    ]);

    expect(buttons.map((button) => button.attributes('data-size'))).toEqual(['icon', 'icon']);
  });

  it('emits edit or delete, and the click never reaches the row that holds them', async () => {
    const rowClicks = vi.fn();
    const Row = defineComponent({
      setup: () => () => h('div', { onClick: rowClicks }, h(RowEditDelete, {
        editLabel: 'Edit',
        deleteLabel: 'Delete',
      })),
    });

    mounted = mount(Row);

    const actions = mounted.getComponent(RowEditDelete);

    await actions.get('[data-row-edit]').trigger('click');
    await actions.get('[data-row-delete]').trigger('click');

    expect(actions.emitted('edit')).toHaveLength(1);
    expect(actions.emitted('delete')).toHaveLength(1);
    expect(rowClicks).not.toHaveBeenCalled();
  });
});
