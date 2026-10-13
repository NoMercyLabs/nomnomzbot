<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { useI18n } from 'vue-i18n'

import { Button } from '@/components/ui/button'
import { Separator } from '@/components/ui/separator'
import { useNavigation } from './useNavigation'

const { t } = useI18n()
const { groups, isCurrent } = useNavigation()
</script>

<template>
  <!-- The icon rail for 600 to 839 px: the shadcn sidebar is not used here, its small-screen sheet has an English title. -->
  <nav
    data-slot="nav-rail"
    :aria-label="t('frame.nav.label')"
    class="flex h-svh w-14 flex-none flex-col items-center gap-1 overflow-y-auto border-r border-sidebar-border bg-sidebar p-2 text-sidebar-foreground"
  >
    <template v-for="(group, index) in groups" :key="group.key">
      <Separator v-if="index > 0" class="my-1" />
      <Button
        v-for="entry in group.entries"
        :key="entry.route"
        :variant="isCurrent(entry) ? 'secondary' : 'ghost'"
        size="icon"
        as-child
      >
        <RouterLink
          :to="{ name: entry.route }"
          :title="t(`frame.nav.${entry.label}`)"
          :aria-label="t(`frame.nav.${entry.label}`)"
        >
          <component :is="entry.icon" class="size-5" aria-hidden="true" />
        </RouterLink>
      </Button>
    </template>
  </nav>
</template>
