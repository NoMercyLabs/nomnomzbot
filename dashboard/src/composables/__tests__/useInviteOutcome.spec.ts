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

import { flushPromises, mount } from '@vue/test-utils';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent } from 'vue';

type ToastCall = [string, { description?: string }];

const toast = vi.fn<(...args: ToastCall) => void>();

vi.mock('vue-sonner', () => ({
  toast: {
    success: (...args: ToastCall): void => toast(...args),
    error: (...args: ToastCall): void => toast(...args),
  },
}));

// The sign-in redirect lands here: the fragment is consumed by the real session code, and the real
// composable reads what it found. Only the toast host and the network are outside.
async function landWith(fragment: string): Promise<void> {
  vi.resetModules();
  history.replaceState(null, '', `/app#${fragment}`);
  const { restoreSession } = await import('@/api/auth');
  const { i18n } = await import('@/i18n');
  const { useInviteOutcome } = await import('../useInviteOutcome');

  await restoreSession();

  mount(
    defineComponent({
      setup() {
        useInviteOutcome();

        return () => null;
      },
    }),
    { global: { plugins: [i18n] } },
  );

  await flushPromises();
}

beforeEach(() => {
  toast.mockClear();
  sessionStorage.clear();
  vi.stubGlobal('fetch', vi.fn(async () => new Response('{}', { status: 401 })));
});

afterEach(() => {
  vi.unstubAllGlobals();
  history.replaceState(null, '', '/');
});

describe('the invite result after a sign-in redirect', () => {
  it('names the gift in one success message when the invite was redeemed', async () => {
    sessionStorage.setItem('nnz.invite.gift', 'Unlimited for 6 months');

    await landWith('access_token=tok&expires_in=900&invite=redeemed');

    expect(toast).toHaveBeenCalledTimes(1);
    expect(toast.mock.calls[0]![0]).toBe('Invite redeemed');
    expect(toast.mock.calls[0]![1].description).toBe('Unlimited for 6 months is now on your channel.');
    expect(sessionStorage.getItem('nnz.invite.gift')).toBeNull();
  });

  it('still says the invite was redeemed when the gift was not remembered', async () => {
    await landWith('access_token=tok&invite=redeemed');

    expect(toast.mock.calls[0]![1].description).toBe('Your invite is now on your channel.');
  });

  it('shows the reason the bot gave when the redeem failed', async () => {
    await landWith('access_token=tok&invite=failed&invite_reason=Invite%20code%20has%20expired.');

    expect(toast).toHaveBeenCalledTimes(1);
    expect(toast.mock.calls[0]![0]).toBe('The invite could not be redeemed');
    expect(toast.mock.calls[0]![1].description).toBe('Invite code has expired.');
  });

  it('shows the message once, and nothing for a sign-in that carried no invite', async () => {
    await landWith('access_token=tok&expires_in=900');

    expect(toast).not.toHaveBeenCalled();
  });
});
