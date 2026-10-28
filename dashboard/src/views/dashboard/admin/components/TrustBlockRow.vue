<!--
  -----------------------------------------------------------------------------
   Copyright (c) NoMercy Labs.

   This file is part of NomNomzBot. You may use, change and share it for free,
   for any noncommercial purpose, under the PolyForm Noncommercial License
   1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
   See LICENSE for details, and TRADEMARKS.md for the name and logo.

   SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
    <div class="min-w-0 flex-1 basis-44">
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
