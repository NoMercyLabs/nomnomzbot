<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The scroll region of a page. The frame's <main> never scrolls, so a page keeps its PageHeader fixed on top
// and puts the rest in this body; the body scrolls when the content is taller than the window.
</script>

<template>
  <div data-slot="page-body" class="flex min-h-0 min-w-0 flex-1 flex-col gap-4 overflow-y-auto p-6 max-md:p-4 max-md:*:shrink-0">
    <slot />
  </div>
</template>
