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
// One automatic account action waiting for a human: who, where, what the bot saw and what it did.
import type { TrustSafetyReviewItem } from '@/api/adminTrustSafety';
import DateTime from '@/components/layout/DateTime.vue';
import UserName from '@/components/layout/UserName.vue';
import { Button } from '@/components/ui/button';

defineProps<{ item: TrustSafetyReviewItem; canReview: boolean }>();

defineEmits<{ confirm: []; overturn: [] }>();
</script>

<template>
  <li data-review-row class="flex flex-wrap items-center gap-x-3 gap-y-2 px-4 py-2">
    <div class="min-w-0 flex-1 basis-64">
      <p class="truncate text-sm">
        <UserName :user-id="item.subjectPlatformUserId" :name="item.subjectDisplayName" />
        <span class="text-muted-foreground"> {{ item.channelName }}</span>
      </p>
      <p class="truncate text-xs text-muted-foreground">{{ item.reason }}</p>
      <p v-if="item.messageText" class="truncate text-xs">{{ item.messageText }}</p>
      <p class="flex flex-wrap gap-x-3 text-xs text-muted-foreground">
        <span>{{ $t(`dashboard.admin.trustSafety.review.confidence.${item.confidence}`) }}</span>
        <span>{{ $t(`dashboard.admin.trustSafety.review.outcome.${item.outcome}`) }}</span>
        <DateTime :value="item.detectedAt" />
      </p>
    </div>
    <div v-if="canReview" class="flex items-center gap-2">
      <Button variant="outline" size="sm" @click="$emit('confirm')">{{
        $t("dashboard.admin.trustSafety.review.confirm")
      }}</Button>
      <Button variant="destructive" size="sm" @click="$emit('overturn')">{{
        $t("dashboard.admin.trustSafety.review.overturn")
      }}</Button>
    </div>
  </li>
</template>
