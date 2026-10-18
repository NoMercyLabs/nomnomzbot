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
import { beforeEach, describe, expect, it, vi } from 'vitest';

import GatedSwitch from '@/components/layout/GatedSwitch.vue';

const held = vi.hoisted(() => ({
  keys: new Set<string>(),
  platformKeys: new Set<string>(),
}));

vi.mock('@/composables/useCan', () => ({
  useCan: () => ({
    known: { value: true },
    can: (key: string) => held.keys.has(key),
  }),
}));

vi.mock('@/composables/usePlatformCan', () => ({
  usePlatformCan: () => ({
    known: { value: true },
    can: (key: string) => held.platformKeys.has(key),
  }),
}));

function gated(modelValue: boolean): VueWrapper {
  return mount(GatedSwitch, {
    props: {
      action: 'feature:write',
      modelValue,
      ariaLabel: 'Chat games',
    },
    attrs: { 'data-setting': 'chat-games' },
    global: { mocks: { $t: (key: string) => key } },
  });
}

describe('a switch gated by a permission', () => {
  beforeEach(() => {
    held.keys.clear();
    held.platformKeys.clear();
  });

  it('is a switch that emits the new value when the person holds the key', async () => {
    held.keys.add('feature:write');

    const wrapper = gated(false);
    const toggle = wrapper.find('[role="switch"]');

    await toggle.trigger('click');

    expect(wrapper.emitted('update:modelValue')).toEqual([[true]]);
    expect(toggle.attributes('aria-label')).toBe('Chat games');
    expect(toggle.attributes('data-setting')).toBe('chat-games');
    expect(wrapper.find('[data-slot="enabled-state"]').exists()).toBe(false);
  });

  it('does not emit while disabled, even with the key', async () => {
    held.keys.add('feature:write');

    const wrapper = mount(GatedSwitch, {
      props: {
        action: 'feature:write',
        modelValue: false,
        disabled: true,
      },
      global: { mocks: { $t: (key: string) => key } },
    });

    await wrapper.find('[role="switch"]').trigger('click');

    expect(wrapper.emitted('update:modelValue')).toBeUndefined();
  });

  it('shows the state as a badge and nothing to press without the key', async () => {
    const wrapper = gated(true);
    const badge = wrapper.find('[data-slot="enabled-state"]');

    await badge.trigger('click');

    expect(wrapper.find('[role="switch"]').exists()).toBe(false);
    expect(badge.attributes('data-state')).toBe('on');
    expect(wrapper.emitted('update:modelValue')).toBeUndefined();
  });

  it('reads the platform keys instead of the channel keys when asked', () => {
    held.keys.add('feature:write');

    const wrapper = mount(GatedSwitch, {
      props: {
        action: 'featureflag:write',
        modelValue: true,
        platform: true,
      },
      global: { mocks: { $t: (key: string) => key } },
    });

    expect(wrapper.find('[role="switch"]').exists()).toBe(false);

    held.platformKeys.add('featureflag:write');

    const allowed = mount(GatedSwitch, {
      props: {
        action: 'featureflag:write',
        modelValue: true,
        platform: true,
      },
    });

    expect(allowed.find('[role="switch"]').exists()).toBe(true);
  });

  it('shows the off state as an off badge without the key', () => {
    const wrapper = gated(false);

    expect(wrapper.find('[data-slot="enabled-state"]').attributes('data-state')).toBe('off');
  });
});
