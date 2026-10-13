<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Self-host or hosted. The license note under the cards must stay visible: hosting NomNomzBot as a
// service for third parties is reserved to NoMercy Labs, while self-hosting your own bot is always free.
import { Check } from '@lucide/vue'
import { useI18n } from 'vue-i18n'

import PanelCard from '@/components/layout/PanelCard.vue'
import { Button } from '@/components/ui/button'

type Option = { id: 'self' | 'hosted'; connect: boolean }

const options: Option[] = [
  { id: 'self', connect: true },
  { id: 'hosted', connect: false },
]
const points: string[] = ['point1', 'point2', 'point3']
const { t } = useI18n()
</script>

<template>
  <section id="deploy" class="flex scroll-mt-6 flex-col gap-6">
    <div>
      <h2 class="text-2xl font-semibold">{{ t('site.home.deploy.title') }}</h2>
      <p class="mt-2 text-base text-muted-foreground">{{ t('site.home.deploy.subtitle') }}</p>
    </div>
    <div class="grid gap-4 md:grid-cols-2">
      <PanelCard
        v-for="option in options"
        :key="option.id"
        :title="t(`site.home.deploy.${option.id}.title`)"
        :tier="option.connect ? 'lead' : 'quiet'"
        pad
      >
        <template #actions>
          <span class="rounded-[8px] border border-border bg-muted px-2 py-1 text-xs font-medium text-muted-foreground">
            {{ t(`site.home.deploy.${option.id}.tag`) }}
          </span>
        </template>
        <ul class="flex flex-col gap-2">
          <li v-for="point in points" :key="point" class="flex items-start gap-2 text-sm">
            <Check class="mt-0.5 size-4 flex-none text-muted-foreground" aria-hidden="true" />
            {{ t(`site.home.deploy.${option.id}.${point}`) }}
          </li>
        </ul>
        <template v-if="option.connect" #footer>
          <Button as-child variant="outline">
            <RouterLink :to="{ name: 'login' }">{{ t('site.home.connect') }}</RouterLink>
          </Button>
        </template>
      </PanelCard>
    </div>
    <p class="rounded-[12px] border border-border bg-muted px-4 py-3 text-sm text-muted-foreground">
      {{ t('site.home.deploy.note') }}
    </p>
  </section>
</template>
