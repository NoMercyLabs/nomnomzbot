<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
