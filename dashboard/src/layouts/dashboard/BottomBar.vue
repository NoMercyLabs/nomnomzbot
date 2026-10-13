<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { Ellipsis, X } from '@lucide/vue'
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'

import { Button } from '@/components/ui/button'
import { Sheet, SheetClose, SheetContent, SheetDescription, SheetHeader, SheetTitle, SheetTrigger } from '@/components/ui/sheet'
import type { NavEntry } from './navigation'
import { useNavigation } from './useNavigation'

const { t } = useI18n()
const { entries, isCurrent } = useNavigation()
const more = ref(false)

// The pages a thumb needs most stay in the bar; the rest open in the sheet.
const barEntries = computed<NavEntry[]>(() => entries.value.filter((entry) => entry.bottom).slice(0, 4))
const sheetEntries = computed<NavEntry[]>(() => entries.value.filter((entry) => !barEntries.value.includes(entry)))
</script>

<template>
  <nav
    data-slot="bottom-bar"
    :aria-label="t('frame.nav.label')"
    class="flex flex-none items-stretch justify-around border-t border-sidebar-border bg-sidebar p-1 text-sidebar-foreground"
  >
    <Button
      v-for="entry in barEntries"
      :key="entry.route"
      :variant="isCurrent(entry) ? 'secondary' : 'ghost'"
      class="h-auto min-h-11 flex-1 flex-col gap-1 px-1 py-1.5 text-xs"
      as-child
    >
      <RouterLink :to="{ name: entry.route }">
        <component :is="entry.icon" aria-hidden="true" />
        <span class="truncate">{{ t(`frame.nav.${entry.label}`) }}</span>
      </RouterLink>
    </Button>
    <Sheet v-if="sheetEntries.length" v-model:open="more">
      <SheetTrigger as-child>
        <Button variant="ghost" class="h-auto min-h-11 flex-1 flex-col gap-1 px-1 py-1.5 text-xs">
          <Ellipsis aria-hidden="true" />
          <span class="truncate">{{ t('frame.nav.more') }}</span>
        </Button>
      </SheetTrigger>
      <SheetContent side="bottom" :show-close-button="false" class="max-h-[80svh]">
        <SheetHeader class="flex-row items-center justify-between">
          <SheetTitle>{{ t('frame.nav.more') }}</SheetTitle>
          <SheetClose as-child>
            <Button variant="ghost" size="icon" :aria-label="t('common.close')">
              <X aria-hidden="true" />
            </Button>
          </SheetClose>
        </SheetHeader>
        <SheetDescription class="sr-only">{{ t('frame.nav.moreHelp') }}</SheetDescription>
        <ul class="flex flex-col gap-1 overflow-y-auto p-2">
          <li v-for="entry in sheetEntries" :key="entry.route">
            <Button :variant="isCurrent(entry) ? 'secondary' : 'ghost'" class="w-full justify-start" as-child>
              <RouterLink :to="{ name: entry.route }" @click="more = false">
                <component :is="entry.icon" aria-hidden="true" />
                {{ t(`frame.nav.${entry.label}`) }}
              </RouterLink>
            </Button>
          </li>
        </ul>
      </SheetContent>
    </Sheet>
  </nav>
</template>
