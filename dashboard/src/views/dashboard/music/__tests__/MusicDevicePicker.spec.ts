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
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, h, ref } from 'vue';

import { i18n } from '@/i18n';

import MusicDevicePicker from '../components/MusicDevicePicker.vue';

const api = vi.hoisted(() => ({
  mutate: vi.fn<(request: { deviceId: string; play: boolean }) => void>(),
  devices: [] as {
    id: string;
    name: string;
    type: string;
    isActive: boolean;
  }[],
}));

vi.mock('@/api/music', () => ({
  useMusicDevices: () => ({ data: ref(api.devices) }),
  useTransferMusicDevice: () => ({ mutate: api.mutate, isPending: ref(false) }),
}));

const passThrough = defineComponent({
  setup: (_, { slots }) => () => h('div', slots.default?.()),
});
const item = defineComponent({
  emits: ['select'],
  setup: (_, { slots, emit, attrs }) => () =>
    h('button', { ...attrs, onClick: () => emit('select') }, slots.default?.()),
});

function mountPicker(startPlaying: boolean) {
  return mount(MusicDevicePicker, {
    props: { channelId: 'c1', startPlaying },
    global: {
      plugins: [i18n],
      stubs: {
        DropdownMenu: passThrough,
        DropdownMenuTrigger: passThrough,
        DropdownMenuContent: passThrough,
        DropdownMenuItem: item,
      },
    },
  });
}

beforeEach(() => {
  api.mutate.mockReset();

  api.devices = [
    {
      id: 'phone',
      name: 'Phone',
      type: 'Smartphone',
      isActive: true,
    },
    {
      id: 'pc',
      name: 'My PC',
      type: 'Computer',
      isActive: false,
    },
  ];
});

describe('the Spotify device picker', () => {
  it('names the active device and moves the music to the chosen one, keeping its state', async () => {
    const wrapper = mountPicker(false);

    expect(wrapper.get('[data-device-picker]').text()).toContain('Phone');

    await wrapper.findAll('[data-device-row]')[1]?.trigger('click');

    expect(api.mutate).toHaveBeenCalledWith({ deviceId: 'pc', play: false });
  });

  it('starts the music on the chosen device when nothing plays anywhere', async () => {
    api.devices = api.devices.map((row) => ({ ...row, isActive: false }));
    const wrapper = mountPicker(true);

    await wrapper.findAll('[data-device-row]')[1]?.trigger('click');

    expect(api.mutate).toHaveBeenCalledWith({ deviceId: 'pc', play: true });
  });

  it('shows nothing when the account has no device to choose', () => {
    api.devices = [];

    expect(mountPicker(true).find('[data-device-picker]').exists()).toBe(false);
  });
});
