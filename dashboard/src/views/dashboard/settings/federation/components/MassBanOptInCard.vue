<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
