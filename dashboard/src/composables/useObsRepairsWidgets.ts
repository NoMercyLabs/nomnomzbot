// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { computed, type ComputedRef } from 'vue';

import { useObsBridge, useObsConnectionStatus } from '@/api/obs';

import { useCan } from './useCan';
import { useCurrentChannel } from './useCurrentChannel';

// Whether the bot itself reloads an OBS browser source whose widget lost its connection (ObsBrowserSourceRecoveryService).
// It can only do that while it controls OBS: the OBS connection is switched on and, in bridge mode, a bridge page is open.
// Whoever may not read the bridge is given the benefit of the doubt, so the page does not warn falsely.
export function useObsRepairsWidgets(): ComputedRef<boolean> {
  const { channelId } = useCurrentChannel();
  const { can } = useCan();
  const status = useObsConnectionStatus(channelId);
  const bridgeChannelId = computed<string | null>(() =>
    can('obs:config:read')
      ? channelId.value
      : null,
  );
  const bridge = useObsBridge(bridgeChannelId);

  return computed<boolean>(() => {
    if (status.data.value?.isEnabled !== true)
      return false;

    if (status.data.value.mode !== 'bridge' || !can('obs:config:read'))
      return true;

    return Number(bridge.data.value?.instanceCount ?? 0) > 0;
  });
}
