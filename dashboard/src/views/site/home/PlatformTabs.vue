<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One command, answered the same way on each platform.
import { useI18n } from 'vue-i18n'

import PlatformLogo from '@/components/layout/PlatformLogo.vue'
import RowGroup from '@/components/layout/RowGroup.vue'
import SummaryRow from '@/components/layout/SummaryRow.vue'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'

const platforms: string[] = ['twitch', 'kick', 'youtube', 'x']
const { t } = useI18n()
</script>

<template>
  <Tabs default-value="twitch" class="gap-2">
    <TabsList :aria-label="t('site.home.outcomes.platforms.label')" class="w-full justify-start overflow-x-auto">
      <TabsTrigger v-for="platform in platforms" :key="platform" :value="platform">
        <PlatformLogo :platform="platform" />
        {{ t(`site.home.outcomes.platforms.${platform}`) }}
      </TabsTrigger>
    </TabsList>
    <TabsContent v-for="platform in platforms" :key="platform" :value="platform">
      <RowGroup>
        <SummaryRow
          :title="t(`site.home.outcomes.platforms.${platform}`)"
          :detail="t('site.home.outcomes.platforms.answered', { platform: t(`site.home.outcomes.platforms.${platform}`) })"
        />
      </RowGroup>
    </TabsContent>
  </Tabs>
</template>
