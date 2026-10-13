<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The mockup panel (V13, V21): a card with a head, a body and an optional foot, split by 1px dividers.
// tier: lead = the one panel the page is for; quiet = status the eye may skip, so its title is muted.
withDefaults(
  defineProps<{ title: string; description?: string; tier?: 'lead' | 'default' | 'quiet'; pad?: boolean }>(),
  { description: undefined, tier: 'default', pad: false },
)
</script>

<template>
  <section class="flex min-h-0 min-w-0 flex-col overflow-hidden rounded-lg border border-border bg-card text-card-foreground">
    <header class="flex min-h-13 flex-none items-center gap-2 border-b border-border px-4 py-2">
      <div class="min-w-0 flex-1">
        <h2
          class="text-base"
          :class="tier === 'quiet' ? 'font-medium text-muted-foreground' : 'font-semibold'"
        >
          {{ title }}
        </h2>
        <p v-if="description" class="text-sm text-muted-foreground">{{ description }}</p>
      </div>
      <slot name="actions" />
    </header>
    <div class="min-h-0 flex-1 overflow-y-auto" :class="pad ? 'p-4' : 'p-2'">
      <slot />
    </div>
    <footer v-if="$slots.footer" class="flex flex-none items-center gap-2 border-t border-border px-4 py-3">
      <slot name="footer" />
    </footer>
  </section>
</template>
