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

import { mount, type VueWrapper } from '@vue/test-utils';
import { describe, expect, it } from 'vitest';

import ConnectionRow, { type ConnectionState } from '@/components/layout/ConnectionRow.vue';
import { i18n } from '@/i18n';

function mountRow(
  state: ConnectionState,
  props: Record<string, unknown> = {},
  slots: Record<string, string> = {},
): VueWrapper {
  return mount(ConnectionRow, {
    props: {
      name: 'Spotify',
      state,
      ...props,
    },
    slots,
    global: { plugins: [i18n] },
  });
}

const badge = (wrapper: VueWrapper): ReturnType<VueWrapper['get']> => wrapper.get('[data-slot="status-badge"]');

describe('the connection row state', () => {
  it('draws a connected service with the ok tone and the connected words', () => {
    const wrapper = mountRow('connected');

    expect(badge(wrapper).attributes('data-tone')).toBe('ok');
    expect(badge(wrapper).text()).toBe('Connected');
    expect(wrapper.attributes('data-state')).toBe('connected');
  });

  it('draws a disconnected service with the quiet off tone and its words', () => {
    const wrapper = mountRow('disconnected');

    expect(badge(wrapper).attributes('data-tone')).toBe('off');
    expect(badge(wrapper).text()).toBe('Not connected');
  });

  it('draws a service that needs the person with the attention tone, never the harm red', () => {
    const wrapper = mountRow('attention');

    expect(badge(wrapper).attributes('data-tone')).toBe('attention');
    expect(badge(wrapper).text()).toBe('Needs attention');
    expect(badge(wrapper).find('[aria-hidden="true"]').classes()).not.toContain('bg-destructive');
  });

  it('shows the exact reason instead of the state words when the page gives one', () => {
    const wrapper = mountRow('attention', { label: 'Sign in again' });

    expect(badge(wrapper).text()).toBe('Sign in again');
    expect(badge(wrapper).attributes('data-tone')).toBe('attention');
  });
});

describe('the connection row content', () => {
  it('shows the name, the connected account and the detail line', () => {
    const wrapper = mountRow('connected', { account: 'stoney_eagle', detail: 'Playback and library' });

    expect(wrapper.get('[data-slot="summary-row-title"]').text()).toContain('Spotify');
    expect(wrapper.get('[data-slot="connection-account"]').text()).toBe('as stoney_eagle');
    expect(wrapper.text()).toContain('Playback and library');
  });

  it('shows no account line for a service without an account', () => {
    expect(mountRow('disconnected').find('[data-slot="connection-account"]').exists()).toBe(false);
  });

  it('draws the platform logo for a known provider and none for an unknown one', () => {
    expect(mountRow('connected', { provider: 'twitch' }).find('svg').exists()).toBe(true);
    expect(mountRow('connected', { provider: 'spotify' }).find('svg').exists()).toBe(false);
    expect(mountRow('connected', { provider: 'spotify' }).find('[data-slot="connection-logo"]').classes()).toContain('size-5');
  });

  it('puts the credential and detail slots under the row and the actions beside it', () => {
    const wrapper = mountRow('connected', {}, {
      credential: '<p data-test="credential">Client ID ending in dbfb</p>',
      detail: '<p data-test="more">Permission sets</p>',
      actions: '<button data-test="reconnect">Reconnect</button>',
    });

    expect(wrapper.get('[data-slot="connection-credential"] [data-test="credential"]').text()).toContain('dbfb');
    expect(wrapper.get('[data-slot="connection-detail"] [data-test="more"]').text()).toBe('Permission sets');
    expect(wrapper.get('[data-slot="summary-row-trailing"] [data-test="reconnect"]').text()).toBe('Reconnect');
  });

  it('draws no slot wrapper for a slot the page leaves empty', () => {
    const wrapper = mountRow('connected');

    expect(wrapper.find('[data-slot="connection-credential"]').exists()).toBe(false);
    expect(wrapper.find('[data-slot="connection-detail"]').exists()).toBe(false);
    expect(wrapper.find('[data-slot="summary-row-trailing"]').exists()).toBe(false);
  });
});

describe('the connection row stays a presentation component', () => {
  it('holds no dialog and no button of its own: the page keeps the flow', () => {
    const wrapper = mountRow('attention', { account: 'stoney_eagle' });

    expect(wrapper.find('button').exists()).toBe(false);
    expect(wrapper.find('[role="alertdialog"]').exists()).toBe(false);
    expect(document.body.querySelector('[role="alertdialog"]')).toBeNull();
  });
});
