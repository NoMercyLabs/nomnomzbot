<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The side panel that holds the delivery log of one outbound webhook.
import type { OutboundWebhook } from '@/api/webhooks';
import SidePanel from '@/components/layout/SidePanel.vue';

import OutboundDeliveriesCard from './OutboundDeliveriesCard.vue';

defineProps<{ endpoint: OutboundWebhook | null }>();

const open = defineModel<boolean>('open', { required: true });
</script>

<template>
  <SidePanel
    v-model:open="open"
    drawer-only
    :label="$t('dashboard.settings.webhooks.deliveries.title')"
  >
    <OutboundDeliveriesCard :endpoint="endpoint" />
  </SidePanel>
</template>
