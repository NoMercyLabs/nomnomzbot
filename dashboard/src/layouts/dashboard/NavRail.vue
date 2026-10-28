<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
import { useI18n } from 'vue-i18n';

import { Button } from '@/components/ui/button';
import { Separator } from '@/components/ui/separator';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';
import { useNavigation } from './useNavigation';

const { t } = useI18n();
const { groups, isCurrent, linkTo } = useNavigation();
</script>

<template>
  <!-- The icon rail for 600 to 839 px: the shadcn sidebar is not used here, its small-screen sheet has an English title. -->
  <nav
    data-slot="nav-rail"
    :aria-label="t('frame.nav.label')"
    class="flex h-svh w-16 flex-none flex-col items-center gap-1.5 overflow-y-auto border-r border-sidebar-border bg-sidebar p-3 text-sidebar-foreground"
  >
    <!-- Square tiles with the name in a tooltip; the current page is a soft accent tile (owner 2026-10-14). -->
    <template v-for="(group, index) in groups" :key="group.key">
      <Separator v-if="index > 0" class="my-1.5" />
      <Tooltip v-for="entry in group.entries" :key="entry.route">
        <TooltipTrigger as-child>
          <Button
            variant="ghost"
            size="icon-lg"
            :class="isCurrent(entry) && 'bg-primary/15 text-primary hover:bg-primary/20 hover:text-primary'"
            as-child
          >
            <RouterLink
              :to="linkTo(entry)"
              :aria-label="t(`frame.nav.${entry.label}`)"
              :aria-current="isCurrent(entry) ? 'page' : undefined"
            >
              <component :is="entry.icon" class="size-5" aria-hidden="true" />
            </RouterLink>
          </Button>
        </TooltipTrigger>
        <TooltipContent side="right">
          {{ t(`frame.nav.${entry.label}`) }}
        </TooltipContent>
      </Tooltip>
    </template>
  </nav>
</template>
