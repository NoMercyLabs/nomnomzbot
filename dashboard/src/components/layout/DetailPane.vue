<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The detail side of a list and detail page: the header (title, status, actions) stays in view and only
// the content below it scrolls (owner 2026-10-13).
</script>

<template>
  <div data-slot="detail-pane" class="flex min-h-0 flex-1 flex-col">
    <header data-slot="detail-pane-header" class="flex-none pb-4">
      <slot name="header" />
    </header>
    <div data-slot="detail-pane-body" class="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto">
      <slot />
    </div>
  </div>
</template>
