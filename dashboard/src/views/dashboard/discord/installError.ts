// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { ref, type Ref } from 'vue';

const param = 'discord_error';

// The reasons the bot names when the Discord install fails (DiscordOAuthController). Each has its own words;
// any other code gets the general message, and the raw code never shows.
export const installErrorCodes = [
  'token_exchange_failed',
  'token_endpoint_unreachable',
  'credentials_unconfigured',
  'access_denied',
  'authorization_failed',
  'invalid_state',
  'no_access_token',
  'no_guild',
  'guild_link_failed',
] as const;

// The reason the last install failed, shared by the page and the install popup that reports into it.
export const installFailure = ref<string | null>(null);

// Without a popup the bot sends a person back here after a failed install with ?discord_error=<reason>. The reason
// is read once and dropped from the address, so a reload does not show an old failure again.
export function readInstallError(): Ref<string | null> {
  const url = new URL(window.location.href);
  const code = url.searchParams.get(param);

  if (code !== null) {
    url.searchParams.delete(param);
    window.history.replaceState(window.history.state, '', url);
  }

  installFailure.value = code;

  return installFailure;
}

export function installErrorKey(code: string): string {
  const known = (installErrorCodes as readonly string[]).includes(code);
  const key = known
    ? code
    : 'unknown';

  return `dashboard.discord.installError.${key}`;
}
