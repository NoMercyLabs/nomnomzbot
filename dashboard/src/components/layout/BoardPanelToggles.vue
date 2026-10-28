<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The show/hide switch of every optional panel on the page's board, drawn once by the page header. The board
// that owns the panels registers them (useBoard); a page with no optional panel draws nothing.
// Below md the switches fold into one Panels menu, so the header stays one compact row.
import MenuDotsHorizontal from '~icons/nnz/menu-dots-horizontal';

import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';

import { boardToggles } from './board';
import LabeledSwitch from './LabeledSwitch.vue';
</script>

<template>
  <template v-if="boardToggles.length">
    <div data-board-toggles-wide class="contents max-md:hidden">
      <LabeledSwitch
        v-for="toggle in boardToggles"
        :key="toggle.id"
        :data-board-toggle="toggle.id"
        :label="toggle.label"
        :model-value="toggle.shown"
        @update:model-value="toggle.set"
      />
    </div>
    <DropdownMenu>
      <DropdownMenuTrigger as-child>
        <Button
          data-board-toggles-menu
          class="md:hidden"
          variant="ghost"
          size="icon"
          :aria-label="$t('frame.panels')"
        >
          <MenuDotsHorizontal class="size-5" aria-hidden="true" />
          <span class="sr-only">{{ $t('frame.panels') }}</span>
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuCheckboxItem
          v-for="toggle in boardToggles"
          :key="toggle.id"
          :data-board-toggle="toggle.id"
          :model-value="toggle.shown"
          @update:model-value="toggle.set"
        >
          {{ toggle.label }}
        </DropdownMenuCheckboxItem>
      </DropdownMenuContent>
    </DropdownMenu>
  </template>
</template>
