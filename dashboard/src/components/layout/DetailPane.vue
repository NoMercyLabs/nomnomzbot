<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
