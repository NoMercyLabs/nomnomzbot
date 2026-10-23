// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
