<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One person in the People list: avatar, name, last seen, and a role icon for moderators and VIPs.
// The list DTO carries only a trust level, so no other role badge can be shown yet.
import Gem from '~icons/nnz/gem'
import Shield from '~icons/nnz/shield'
import { useTimeAgoIntl } from '@vueuse/core'
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'

import type { CommunityRow } from '@/api/people'
import StatusBadge from '@/components/layout/StatusBadge.vue'
import SummaryRow from '@/components/layout/SummaryRow.vue'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'

const props = defineProps<{ person: CommunityRow; selected: boolean }>()
defineEmits<{ pick: [] }>()

const { t, locale } = useI18n()
const ago = useTimeAgoIntl(() => props.person.lastSeen, { locale: locale.value })

const lastSeen: ComputedRef<string> = computed<string>(() =>
  t('dashboard.people.list.lastSeen', { ago: ago.value }),
)
const initials: ComputedRef<string> = computed<string>(() =>
  props.person.displayName.slice(0, 2).toUpperCase(),
)
</script>

<template>
  <div data-person-row class="px-1 py-0.5">
    <SummaryRow
      pick
      :selected="selected"
      :aria-pressed="selected"
      :title="person.displayName"
      :detail="lastSeen"
      @click="$emit('pick')"
    >
      <template #leading>
        <Avatar class="size-9">
          <AvatarImage
            v-if="person.profileImageUrl"
            :src="person.profileImageUrl"
            :alt="person.displayName"
          />
          <AvatarFallback>{{ initials }}</AvatarFallback>
        </Avatar>
      </template>
      <template #end>
        <StatusBadge
          v-if="person.isBanned"
          tone="bad"
          :label="$t('dashboard.people.list.banned')"
        />
        <Shield
          v-if="person.trustLevel === 'moderator'"
          role="img"
          class="size-4 text-muted-foreground"
          :aria-label="$t('dashboard.people.list.roles.moderator')"
        />
        <Gem
          v-else-if="person.trustLevel === 'vip'"
          role="img"
          class="size-4 text-muted-foreground"
          :aria-label="$t('dashboard.people.list.roles.vip')"
        />
      </template>
    </SummaryRow>
  </div>
</template>
