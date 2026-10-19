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
// One account flagged in several channels. The chevron opens the detections, one line per channel.
import { ref } from 'vue';
import ChevronDown from '~icons/nnz/chevron-down';
import ChevronRight from '~icons/nnz/chevron-right';

import type { CrossTenantAbuseSignal } from '@/api/adminTrustSafety';
import DateTime from '@/components/layout/DateTime.vue';
import UserName from '@/components/layout/UserName.vue';
import { Button } from '@/components/ui/button';
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible';

defineProps<{ signal: CrossTenantAbuseSignal }>();

const open = ref(false);
</script>

<template>
  <li data-signal-row class="px-4 py-2">
    <Collapsible v-model:open="open">
      <div class="flex items-center gap-3">
        <div class="min-w-0 flex-1">
          <p class="flex min-w-0 items-center gap-1.5 text-sm">
            <UserName :user-id="signal.subjectPlatformUserId" :name="signal.subjectDisplayName" />
            <span class="text-muted-foreground">{{ signal.provider }}</span>
          </p>
          <p class="text-xs text-muted-foreground">
            {{
              $t("dashboard.admin.trustSafety.signals.summary", {
                tenants: Number(signal.tenantCount),
                detections: Number(signal.detectionCount),
              })
            }}
          </p>
        </div>
        <CollapsibleTrigger as-child>
          <Button
            variant="ghost"
            size="icon"
            :aria-label="$t('dashboard.admin.trustSafety.signals.toggle')"
            :aria-expanded="open"
          >
            <ChevronDown v-if="open" class="size-5" aria-hidden="true" />
            <ChevronRight v-else class="size-5" aria-hidden="true" />
          </Button>
        </CollapsibleTrigger>
      </div>
      <CollapsibleContent>
        <ul class="mt-1 grid gap-1 border-l border-border pl-3">
          <li
            v-for="hit in signal.hits"
            :key="hit.detectionId"
            data-signal-hit
            class="flex flex-wrap gap-x-3 text-xs text-muted-foreground"
          >
            <span class="text-foreground">{{ hit.channelName }}</span>
            <span>{{ hit.reason }}</span>
            <span>{{ $t(`dashboard.admin.trustSafety.review.outcome.${hit.outcome}`) }}</span>
            <DateTime :value="hit.detectedAt" />
          </li>
        </ul>
      </CollapsibleContent>
    </Collapsible>
  </li>
</template>
