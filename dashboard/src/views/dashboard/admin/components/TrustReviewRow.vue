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
// One automatic account action waiting for a human: who, where, what the bot saw and what it did.
import type { TrustSafetyReviewItem } from '@/api/adminTrustSafety';
import DateTime from '@/components/layout/DateTime.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import UserName from '@/components/layout/UserName.vue';
import { Button } from '@/components/ui/button';

defineProps<{ item: TrustSafetyReviewItem; canReview: boolean }>();

defineEmits<{ confirm: []; overturn: [] }>();
</script>

<template>
  <div data-review-row>
    <SummaryRow wrap truncate :title="item.subjectDisplayName">
      <template #title>
        <span class="flex min-w-0 items-baseline gap-1.5">
          <UserName :user-id="item.subjectPlatformUserId" :name="item.subjectDisplayName" />
          <span class="min-w-0 truncate font-normal text-muted-foreground">{{ item.channelName }}</span>
        </span>
      </template>
      <template #meta>
        <span class="block truncate">{{ item.reason }}</span>
        <span v-if="item.messageText" class="block truncate text-foreground">{{ item.messageText }}</span>
        <span class="flex flex-wrap gap-x-3">
          <span>{{ $t(`dashboard.admin.trustSafety.review.confidence.${item.confidence}`) }}</span>
          <span>{{ $t(`dashboard.admin.trustSafety.review.outcome.${item.outcome}`) }}</span>
          <DateTime :value="item.detectedAt" />
        </span>
      </template>
      <template v-if="canReview" #trailing>
        <Button variant="outline" size="sm" @click="$emit('confirm')">{{
          $t("dashboard.admin.trustSafety.review.confirm")
        }}</Button>
        <Button variant="outline" size="sm" @click="$emit('overturn')">{{
          $t("dashboard.admin.trustSafety.review.overturn")
        }}</Button>
      </template>
    </SummaryRow>
  </div>
</template>
