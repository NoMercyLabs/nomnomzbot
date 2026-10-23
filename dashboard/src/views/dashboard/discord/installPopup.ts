// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

export interface InstallOutcome {
  // The reason the bot names when the install failed; null when it finished or the person closed the window.
  error: string | null;
}

interface RelayMessage {
  type?: string;
  discord_error?: string;
}

const popupName = 'nnz-discord-install';
const popupSize = 'popup=yes,width=520,height=760';

// The install runs in its own window, so the dashboard stays where it is. The relay page the bot ends on tells
// this window what happened and closes itself; closing the window by hand also settles it. Returns false when the
// browser blocked the popup, so the caller can navigate to the address instead.
export function openInstallPopup(
  url: string,
  onSettled: (outcome: InstallOutcome) => void,
): boolean {
  const popup = window.open(url, popupName, popupSize);

  if (popup === null) {
    return false;
  }

  const origin = new URL(url, window.location.href).origin;
  let timer = 0;

  function finish(error: string | null): void {
    window.removeEventListener('message', onMessage);
    window.clearInterval(timer);
    onSettled({ error });
  }

  function onMessage(event: MessageEvent<RelayMessage | undefined>): void {
    if (event.source !== popup || event.origin !== origin || event.data?.type !== 'oauth_relay') {
      return;
    }

    finish(event.data.discord_error ?? null);
  }

  timer = window.setInterval(() => {
    if (popup.closed) {
      finish(null);
    }
  }, 500);

  window.addEventListener('message', onMessage);

  return true;
}
