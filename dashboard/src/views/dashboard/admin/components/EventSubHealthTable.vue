<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
