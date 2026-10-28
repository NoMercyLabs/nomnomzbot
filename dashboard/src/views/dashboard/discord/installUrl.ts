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

import { computed, toValue, type ComputedRef, type MaybeRefOrGetter } from 'vue';

import { activeBot } from '@/api/activeBot';

// The start route answers with a redirect to Discord's own page, so it is a plain link, not a request.
export function useDiscordInstallUrl(
  channelId: MaybeRefOrGetter<string | null>,
): ComputedRef<string> {
  return computed<string>(() => {
    const channel = toValue(channelId) ?? '';

    return `${activeBot.value}/api/v1/channels/${channel}/integrations/discord/callback/start`;
  });
}
