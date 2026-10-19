<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One spam-defense knob: its name, what it does and what moving it costs on the left, its control on the right.
// A knob the engine does not read yet carries a badge and the reason, so the page never claims an effect it lacks.
import { Badge } from '@/components/ui/badge';

defineProps<{
  settingKey: string;
  label: string;
  explanation: string;
  cost: string;
  inactiveBadge?: string;
  inactiveReason?: string;
  invalidText?: string;
}>();
</script>

<template>
  <div data-spam-setting :data-setting="settingKey" class="flex items-start gap-4 px-4 py-3 max-md:flex-col max-md:gap-2">
    <div class="min-w-0 flex-1">
      <div class="flex flex-wrap items-center gap-2">
        <span class="text-sm font-medium wrap-break-word">{{ label }}</span>
        <Badge v-if="inactiveBadge" variant="secondary" data-inactive>{{ inactiveBadge }}</Badge>
      </div>
      <p class="text-sm text-muted-foreground">{{ explanation }}</p>
      <p class="text-sm text-muted-foreground">{{ cost }}</p>
      <p v-if="inactiveReason" class="text-sm text-muted-foreground">{{ inactiveReason }}</p>
      <p v-if="invalidText" role="alert" data-invalid class="text-sm text-destructive">{{ invalidText }}</p>
    </div>
    <div class="flex shrink-0 items-center gap-2 md:w-40 md:justify-end">
      <slot />
    </div>
  </div>
</template>
