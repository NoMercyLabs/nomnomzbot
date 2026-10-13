<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import Plus from '~icons/nnz/add'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter, type RouteLocationNormalizedLoaded, type Router } from 'vue-router'

import { useCommands, useDeleteCommand, type CommandListItem } from '@/api/commands'
import ListDetail from '@/components/layout/ListDetail.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import PageTabs, { type PageTab } from '@/components/layout/PageTabs.vue'
import PanelCard from '@/components/layout/PanelCard.vue'
import { Button } from '@/components/ui/button'
import { useCurrentChannel } from '@/composables/useCurrentChannel'
import { usePageView } from '@/composables/usePageView'

import BuiltinList from './components/BuiltinList.vue'
import CommandEditor from './components/CommandEditor.vue'
import CommandList from './components/CommandList.vue'
import NewCommandDialog from './components/NewCommandDialog.vue'

const { t } = useI18n()
const route: RouteLocationNormalizedLoaded = useRoute()
const router: Router = useRouter()
const { channelId } = useCurrentChannel()
const commands = useCommands(channelId)
const remove = useDeleteCommand(channelId)

const tabs: ComputedRef<PageTab[]> = computed<PageTab[]>(() => [
  { value: 'custom', label: t('dashboard.commands.tabs.custom') },
  { value: 'builtin', label: t('dashboard.commands.tabs.builtin') },
])
const view = usePageView(['custom', 'builtin'])

// The open command lives in the address (?command=), so a refresh or a link keeps it.
const selected: ComputedRef<string | null> = computed<string | null>(() =>
  typeof route.query.command === 'string' ? route.query.command : null,
)
const row: ComputedRef<CommandListItem | undefined> = computed<CommandListItem | undefined>(() =>
  (commands.data.value ?? []).find(
    (item: CommandListItem): boolean => item.name === selected.value,
  ),
)

function select(name: string | undefined): void {
  void router.push({ query: { ...route.query, command: name } })
}

function removeCommand(name: string): void {
  select(undefined)
  remove.mutate({ commandName: name })
}

const creating: Ref<boolean> = ref(false)
</script>

<template>
  <PageHeader :title="$t('dashboard.commands.title')" :crumb="$t('dashboard.commands.crumb')">
    <template #actions>
      <Button type="button" variant="outline" size="sm" @click="creating = true">
        <Plus aria-hidden="true" />
        {{ $t('dashboard.commands.new') }}
      </Button>
    </template>
    <template #tabs>
      <PageTabs :tabs="tabs" />
    </template>
  </PageHeader>
  <ListDetail v-if="view === 'custom'" :show-detail="row !== undefined">
    <template #list>
      <CommandList :selected="selected" @pick="select" @create="creating = true" />
    </template>
    <template #detail>
      <CommandEditor v-if="row" :key="row.name" :row="row" @remove="removeCommand" />
      <PanelCard
        v-else
        state="empty"
        :title="$t('dashboard.commands.list.pickTitle')"
        :empty-text="$t('dashboard.commands.list.pick')"
      />
    </template>
  </ListDetail>
  <BuiltinList v-else />
  <NewCommandDialog v-model:open="creating" @created="select" />
</template>
