<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The channels of the EventSub registry, one row each. The topic detail lives behind each row's toggle.
import type { AdminEventSubTenantHealth } from '@/api/adminActivity';

import EventSubChannelRow from './EventSubChannelRow.vue';

defineProps<{ rows: readonly AdminEventSubTenantHealth[] }>();
</script>

<template>
  <div>
    <EventSubChannelRow v-for="tenant in rows" :key="tenant.broadcasterId" :tenant="tenant" />
  </div>
</template>
