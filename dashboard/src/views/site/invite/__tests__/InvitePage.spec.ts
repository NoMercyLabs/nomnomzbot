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

import { VueQueryPlugin } from '@tanstack/vue-query';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia } from 'pinia';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import words from '../i18n/en.json';

type Seen = {
  method: string;
  path: string;
  body: unknown;
};

const seen: Seen[] = [];
const assign = vi.fn<(url: string) => void>();

const unusable = {
  isUsable: false,
  notUsableReason: 'This invite link is not valid or can no longer be used.',
  tierDisplayName: null,
  months: null,
  forever: false,
};

const sixMonths = {
  isUsable: true,
  notUsableReason: null,
  tierDisplayName: 'Unlimited',
  months: 6,
  forever: false,
};

const foreverGift = {
  ...sixMonths,
  months: null,
  forever: true,
};

const channelRow = {
  id: 'CH123',
  login: 'stoney_eagle',
  displayName: 'Stoney_Eagle',
  role: 'broadcaster',
  isLive: false,
};

let invite: typeof sixMonths | typeof unusable = sixMonths;
let redeemAnswer: Response = Response.json({
  data: {
    grantedFoundersBadge: false,
    grantedTierKey: 'unlimited',
    foundersBadge: null,
  },
});

// The fake outside world: the bot's answers by method and path. Every request is written down.
async function answer(request: Request): Promise<Response> {
  const path = new URL(request.url).pathname;
  const text = await request.clone().text();

  seen.push({
    method: request.method,
    path,
    body: text === ''
      ? null
      : JSON.parse(text),
  });

  if (path.startsWith('/api/v1/billing/invites/'))
    return Response.json({ data: invite });

  if (path === '/api/v1/channels')
    return Response.json({ data: [channelRow] });

  if (path === '/api/v1/channels/moderated')
    return Response.json({ data: [] });

  if (path.endsWith('/billing/invite/redeem'))
    return redeemAnswer.clone();

  return new Response('{}', { status: 401 });
}

async function open(code: string, signedIn: boolean): Promise<VueWrapper> {
  vi.resetModules();
  const { i18n } = await import('@/i18n');

  i18n.global.mergeLocaleMessage('en', words);

  if (signedIn) {
    (await import('@/api/auth')).acceptAccessToken('test-token');
  }

  const { queryClient } = await import('@/api/queryClient');
  const { createMemoryHistory, createRouter } = await import('vue-router');
  const InvitePage = (await import('../InvitePage.vue')).default;
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/invite/:code', component: InvitePage },
      { path: '/', component: { template: '<div />' } },
    ],
  });

  await router.push(`/invite/${code}`);
  const wrapper = mount(InvitePage, {
    global: { plugins: [createPinia(), [VueQueryPlugin, { queryClient }], i18n, router] },
  });

  await flushPromises();

  return wrapper;
}

// A missing control is a failed test with its name, never a silent skip.
function control(wrapper: VueWrapper, selector: string): ReturnType<VueWrapper['find']> {
  const found = wrapper.find(selector);

  if (!found.exists())
    throw new Error(`No ${selector} on the invite page. Text: ${wrapper.text()}`);

  return found;
}

function requestsTo(path: string): Seen[] {
  return seen.filter((request) => request.path === path);
}

beforeEach(() => {
  seen.length = 0;
  assign.mockClear();
  invite = sixMonths;

  redeemAnswer = Response.json({
    data: {
      grantedFoundersBadge: false,
      grantedTierKey: 'unlimited',
      foundersBadge: null,
    },
  });

  vi.stubGlobal('fetch', vi.fn<(request: Request) => Promise<Response>>(answer));
  vi.stubGlobal('location', { ...window.location, assign });
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('the invite page, signed out', () => {
  it('reads the invite without signing in and shows the tier and the length', async () => {
    const wrapper = await open('WELCOME6', false);

    expect(requestsTo('/api/v1/billing/invites/WELCOME6')).toHaveLength(1);
    expect(wrapper.text()).toContain('Unlimited for 6 months');
    expect(requestsTo('/api/v1/channels')).toHaveLength(0);
  });

  it('says forever for an invite with no end date', async () => {
    invite = foreverGift;
    const wrapper = await open('FOREVER1', false);

    expect(wrapper.text()).toContain('Unlimited forever');
  });

  it('offers one button, and it starts the Twitch sign-in with the invite and a return to the app', async () => {
    const wrapper = await open('WELCOME6', false);

    expect(wrapper.findAll('[data-action]')).toHaveLength(1);
    expect(control(wrapper, '[data-action="sign-in"]').text()).toBe('Sign in with Twitch to claim');

    await control(wrapper, '[data-action="sign-in"]').trigger('click');

    expect(assign).toHaveBeenCalledExactlyOnceWith(
      `${window.location.origin}/api/v1/auth/twitch?client=web&return_to=%2Fapp&role=broadcaster&invite=WELCOME6`,
    );

    expect(requestsTo('/api/v1/channels/CH123/billing/invite/redeem')).toHaveLength(0);
  });

  it('remembers what the invite gives, so the page after sign-in can name it', async () => {
    const wrapper = await open('WELCOME6', false);

    await control(wrapper, '[data-action="sign-in"]').trigger('click');

    expect(sessionStorage.getItem('nnz.invite.gift')).toBe('Unlimited for 6 months');
  });
});

describe('the invite page, with a code that cannot be used', () => {
  it('says so in plain words, offers no sign-in or redeem and links home', async () => {
    invite = unusable;
    const wrapper = await open('NOPE', false);

    expect(wrapper.text()).toContain('This invite link is not valid or can no longer be used.');
    expect(wrapper.find('[data-action="sign-in"]').exists()).toBe(false);
    expect(wrapper.find('[data-action="redeem"]').exists()).toBe(false);
    expect(control(wrapper, '[data-action="home"]').attributes('href')).toBe('/');
  });
});

describe('the invite page, signed in', () => {
  it('shows the same card and a Redeem button for the current channel, with no sign-in start', async () => {
    const wrapper = await open('WELCOME6', true);

    expect(wrapper.text()).toContain('Unlimited for 6 months');
    expect(control(wrapper, '[data-action="redeem"]').text()).toBe('Redeem on Stoney_Eagle');
    expect(wrapper.find('[data-action="sign-in"]').exists()).toBe(false);
    expect(assign).not.toHaveBeenCalled();
  });

  it('redeems on that channel, then names the tier the channel now has', async () => {
    const wrapper = await open('WELCOME6', true);

    await control(wrapper, '[data-action="redeem"]').trigger('click');
    await flushPromises();

    const redeems = requestsTo('/api/v1/channels/CH123/billing/invite/redeem');

    expect(redeems).toHaveLength(1);
    expect(redeems[0]!.method).toBe('POST');
    expect(redeems[0]!.body).toEqual({ code: 'WELCOME6' });

    expect(control(wrapper, '[data-result="redeemed"]').text()).toContain(
      'Stoney_Eagle now has Unlimited for 6 months.',
    );

    expect(wrapper.find('[data-action="redeem"]').exists()).toBe(false);
  });

  it('shows the refusal and keeps the button when the bot refuses the code', async () => {
    redeemAnswer = Response.json(
      {
        title: 'Conflict',
        status: 409,
        detail: 'You have already redeemed this code.',
      },
      { status: 409 },
    );

    const wrapper = await open('WELCOME6', true);

    await control(wrapper, '[data-action="redeem"]').trigger('click');
    await flushPromises();

    expect(control(wrapper, '[role="alert"]').text()).toContain('already redeemed');
    expect(wrapper.find('[data-action="redeem"]').exists()).toBe(true);
  });
});
