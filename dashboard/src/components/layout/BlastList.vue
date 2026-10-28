<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The blast box of a destructive confirm (mockup .blast): what happens when the user presses the red button.
// It is absent with no items. The 12px radius sits inside the dialog body, which has no padding of its own.
defineProps<{ items: readonly string[] }>();
</script>

<template>
  <div
    v-if="items.length > 0"
    data-slot="blast-list"
    class="grid gap-2 rounded-[12px] border border-destructive/35 bg-destructive/8 px-4 py-3 text-sm"
  >
    <b class="font-medium">{{ $t('common.confirm.whatHappens') }}</b>
    <ul class="grid list-disc gap-0.5 pl-[18px]">
      <li v-for="item in items" :key="item">{{ item }}</li>
    </ul>
  </div>
</template>
