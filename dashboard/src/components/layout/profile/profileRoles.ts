// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { Component } from 'vue';
import Crown from '~icons/nnz/crown';
import Diamond from '~icons/nnz/diamond';

import type { CommunityMember } from '@/api/community';
import type { PersonProfile } from '@/api/people';

export type RoleKey = 'broadcaster' | 'moderator' | 'vip' | 'subscriber';

// Twitch's own badge per role in its own color (owner 2026-10-14). A subscriber and a moderator carry
// the platform logo instead: both roles belong to one platform.
export const roleIcons = {
  broadcaster: { icon: Crown, color: 'text-role-broadcaster' },
  vip: { icon: Diamond, color: 'text-role-vip' },
} as const satisfies Record<Exclude<RoleKey, 'subscriber' | 'moderator'>, {
  icon: Component;
  color: string;
}>;

// Twitch sends a subscription tier as its code ("1000"); a person reads it as its number.
const tierNumbers = {
  1000: 1,
  2000: 2,
  3000: 3,
} as const satisfies Record<number, number>;

// The tier number for a Twitch tier code, or null for a code Twitch has not defined.
export function tierNumber(code: string | number | null | undefined): number | null {
  return tierNumbers[Number(code) as keyof typeof tierNumbers] ?? null;
}

// The broadcaster is the channel's own account (a Twitch login is unique); a stored role row never makes
// someone else the broadcaster.
export function isChannelOwner(
  person: CommunityMember,
  channelLogin: string | null | undefined,
): boolean {
  return Boolean(channelLogin) && person.username.toLowerCase() === channelLogin?.toLowerCase();
}

// The roles the bot can prove for a person: the channel's own account, Twitch's own answers (isVip,
// isModerator), the channel role the bot stores, and the subscription the bot has seen. A role it cannot
// prove is left out.
export function roleKeys(
  person: CommunityMember,
  profile: PersonProfile | null,
  channelLogin: string | null | undefined,
): RoleKey[] {
  const stored = profile?.identity.managementRole?.toLowerCase();
  const roles: RoleKey[] = [];

  if (isChannelOwner(person, channelLogin)) {
    roles.push('broadcaster');
  }

  if (stored === 'moderator' || person.isModerator === true) {
    roles.push('moderator');
  }

  if (person.isVip === true) {
    roles.push('vip');
  }

  if (profile?.activity?.isSubscriber === true || Boolean(profile?.identity.subTier)) {
    roles.push('subscriber');
  }

  return roles;
}

export type SanctionTarget = { username: string; roles: readonly RoleKey[] };

function sameLogin(a: string | null | undefined, b: string | null | undefined): boolean {
  return Boolean(a) && Boolean(b) && a?.toLowerCase() === b?.toLowerCase();
}

// Whether the signed-in person can time out or ban this target. Twitch refuses it on yourself and on the
// broadcaster, and only the broadcaster can sanction a moderator (a lead moderator too). Delete stays open.
export function canSanction(
  me: string | null | undefined,
  channelLogin: string | null | undefined,
  target: SanctionTarget,
): boolean {
  if (sameLogin(me, target.username) || sameLogin(channelLogin, target.username))
    return false;

  if (target.roles.includes('broadcaster'))
    return false;

  return !target.roles.includes('moderator') || sameLogin(me, channelLogin);
}

export type TwitchStanding = 'regular' | 'affiliate' | 'partner';

const standings = ['affiliate', 'partner'] as const satisfies TwitchStanding[];

// The person's Twitch channel standing. Twitch sends "" for a regular channel, and the bot reads it together
// with the account age, so without an account age the bot does not know yet.
export function twitchStanding(profile: PersonProfile | null): TwitchStanding | null {
  const identity = profile?.identity;

  if (!identity?.accountCreatedAtUtc)
    return null;

  return standings.find((standing: TwitchStanding) =>
    standing === identity.broadcasterType) ?? 'regular';
}
