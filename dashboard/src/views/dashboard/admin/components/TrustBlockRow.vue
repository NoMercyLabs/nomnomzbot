<!--
  -----------------------------------------------------------------------------
   Copyright (c) NoMercy Labs.

   This file is part of NomNomzBot, free software licensed under the GNU Affero
   General Public License v3.0 or later. You may redistribute and/or modify it
   under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

   SPDX-License-Identifier: AGPL-3.0-or-later
  -----------------------------------------------------------------------------
-->
<script setup lang="ts">
// One network block: who, how far it reached, why, and when. Lifting is for operators who may manage blocks.
import type { NetworkBlock } from '@/api/adminTrustSafety';
import DateTime from '@/components/layout/DateTime.vue';
import UserName from '@/components/layout/UserName.vue';
import { Button } from '@/components/ui/button';

defineProps<{ block: NetworkBlock; canLift: boolean }>();

defineEmits<{ lift: [] }>();
</script>

<template>
  <li data-block-row class="flex flex-wrap items-center gap-x-3 gap-y-2 px-4 py-2">
    <div class="min-w-0 flex-1 basis-64">
      <p class="truncate text-sm">
        <UserName
          :user-id="block.targetTwitchUserId"
          :name="block.targetDisplayName ?? block.targetTwitchUserId"
        />
      </p>
      <p class="flex flex-wrap gap-x-3 text-xs text-muted-foreground">
        <span>{{
          $t("dashboard.admin.trustSafety.blocks.summary", {
            status: $t(`dashboard.admin.trustSafety.blocks.status.${block.status}`),
            count: Number(block.channelCount),
          })
        }}</span>
        <DateTime :value="block.appliedAt" />
      </p>
      <p v-if="block.reason" class="truncate text-xs text-muted-foreground">{{ block.reason }}</p>
    </div>
    <Button v-if="canLift" variant="outline" size="sm" @click="$emit('lift')">{{
      $t("dashboard.admin.trustSafety.blocks.lift")
    }}</Button>
  </li>
</template>
