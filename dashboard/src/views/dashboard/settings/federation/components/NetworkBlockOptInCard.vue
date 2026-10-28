<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Whether an admin network block also bans its blocked actor in this channel. The bot starts at off, so a block
// never reaches a channel that did not say yes. Only the broadcaster can change it; the shown value is always
// the one the bot stores.
import { useNetworkBlockOptIn, useSaveNetworkBlockOptIn } from '@/api/networkBlockOptIn';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import OptInCard from './OptInCard.vue';

const { channelId } = useCurrentChannel();
const optIn = useNetworkBlockOptIn(channelId);
const save = useSaveNetworkBlockOptIn(channelId);
</script>

<template>
  <OptInCard
    data-view="network-block-opt-in"
    scope="dashboard.settings.federation.networkBlock"
    action="moderation:networkblock:optin"
    :opt-in="optIn"
    :save="(accepts: boolean) => save.mutateAsync({ accepts })"
    :pending="save.isPending.value"
  />
</template>
