<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Whether a moderator's mass ban across their channels also reaches this channel. Only the broadcaster can change
// it; the shown value is always the one the bot stores.
import { useMassBanOptIn, useSaveMassBanOptIn } from '@/api/massBanOptIn';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import OptInCard from './OptInCard.vue';

const { channelId } = useCurrentChannel();
const optIn = useMassBanOptIn(channelId);
const save = useSaveMassBanOptIn(channelId);
</script>

<template>
  <OptInCard
    data-view="mass-ban-opt-in"
    scope="dashboard.settings.federation.massBan"
    action="moderation:massban:optout"
    :opt-in="optIn"
    :save="(accepts: boolean) => save.mutateAsync({ accepts })"
    :pending="save.isPending.value"
  />
</template>
