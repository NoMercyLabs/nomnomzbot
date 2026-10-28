<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One person in the People list: avatar, name, last seen, and a role icon for moderators and VIPs.
// The list DTO carries only a trust level, so no other role badge can be shown yet.
import Gem from '~icons/nnz/gem';
import Gift from '~icons/nnz/gift-card';
import Shield from '~icons/nnz/shield';
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { CommunityRow } from '@/api/people';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar';
import { useFormattedTime } from '@/composables/useTime';

const props = defineProps<{ person: CommunityRow; selected: boolean }>();

defineEmits<{ pick: [] }>();

const { t } = useI18n();
const ago = useFormattedTime(() => props.person.lastSeen, 'relative', { live: true });

const lastSeen = computed<string>(() =>
  props.person.lastSeen
    ? t('dashboard.people.list.lastSeen', { ago: ago.value })
    : t('dashboard.people.list.neverSeen'),
);
const initials = computed<string>(() => props.person.displayName.slice(0, 2).toUpperCase());
</script>

<template>
  <div data-person-row :data-gifted="person.gifted" class="px-1 py-0.5">
    <SummaryRow
      pick
      truncate
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
        <Gift
          v-if="person.gifted"
          role="img"
          class="size-4 text-muted-foreground"
          :aria-label="$t('dashboard.people.list.gifted')"
        />
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
