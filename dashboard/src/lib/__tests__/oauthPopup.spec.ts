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

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { openOAuthPopup, type OAuthOutcome } from '../oauthPopup';

interface FakePopup {
  closed: boolean;
}

const url = 'https://accounts.spotify.test/authorize?client_id=x';

let popup: FakePopup;
let opened: ReturnType<typeof vi.spyOn>;

function relay(data: Record<string, string>, source: FakePopup = popup): void {
  window.dispatchEvent(
    new MessageEvent('message', {
      data: { type: 'oauth_relay', ...data },
      origin: window.location.origin,
      source: source as unknown as MessageEventSource,
    }),
  );
}

beforeEach(() => {
  vi.useFakeTimers();
  popup = { closed: false };
  opened = vi.spyOn(window, 'open').mockReturnValue(popup as unknown as Window);
});

afterEach(() => {
  vi.useRealTimers();
  opened.mockRestore();
});

describe('the OAuth popup', () => {
  it('opens the address in its own small window', () => {
    openOAuthPopup(url, vi.fn<(outcome: OAuthOutcome) => void>());

    expect(opened).toHaveBeenCalledWith(url, 'nnz-oauth', expect.stringContaining('width='));
  });

  it('reports a blocked popup so the caller can navigate instead', () => {
    opened.mockReturnValue(null);

    expect(openOAuthPopup(url, vi.fn<(outcome: OAuthOutcome) => void>())).toBe(false);
  });

  it('settles with no error when the relay page says it connected', () => {
    const settled = vi.fn<(outcome: OAuthOutcome) => void>();

    openOAuthPopup(url, settled);

    relay({ discord_connected: 'true' });

    expect(settled).toHaveBeenCalledExactlyOnceWith({ error: null });
  });

  it.each([
    ['discord_error', 'token_exchange_failed'],
    ['error', 'access_denied'],
  ])('settles with the reason when the relay page reports %s', (key, reason) => {
    const settled = vi.fn<(outcome: OAuthOutcome) => void>();

    openOAuthPopup(url, settled);

    relay({ [key]: reason });

    expect(settled).toHaveBeenCalledExactlyOnceWith({ error: reason });
  });

  it('ignores a message from any other window', () => {
    const settled = vi.fn<(outcome: OAuthOutcome) => void>();

    openOAuthPopup(url, settled);

    relay({ discord_error: 'invalid_state' }, { closed: false });

    expect(settled).not.toHaveBeenCalled();
  });

  it('settles with no error, once, when the person closes the popup themselves', () => {
    const settled = vi.fn<(outcome: OAuthOutcome) => void>();

    openOAuthPopup(url, settled);

    popup.closed = true;
    vi.advanceTimersByTime(1500);

    expect(settled).toHaveBeenCalledExactlyOnceWith({ error: null });
  });
});
