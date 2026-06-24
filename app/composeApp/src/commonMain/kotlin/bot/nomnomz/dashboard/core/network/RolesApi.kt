// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

import kotlinx.serialization.Serializable

// The typed roles & permits facade — the channel's bot IAM (roles-permissions §5). It surfaces three real
// backend reads (the management members, the active per-user permit grants, the per-action permission matrix)
// and the four writes the page drives: assign a management role to a user, grant a user a single capability
// (an action key), and revoke either. Every call hits the real channel-scoped routes; nothing is fabricated.
// State holders depend on the interface (the existing "depend on interfaces" convention) and fake it in tests
// without HTTP.
//
// Backend routes:
//   RolesController            (api/v1/channels/{channelId}/roles)
//     GET    /roles                          →  PaginatedResponse<ChannelMembershipDto>
//     PUT    /roles            (SetRoleBody)  →  StatusResponseDto<ChannelMembershipDto>
//     DELETE /roles/{userId}                  →  StatusResponseDto (204-style)
//   PermitsController          (api/v1/channels/{channelId}/permits)
//     GET    /permits                          →  StatusResponseDto<List<PermitGrantDto>>
//     POST   /permits/role        (GrantRoleBody)        →  StatusResponseDto<PermitGrantDto>
//     POST   /permits/capability  (GrantCapabilityBody)  →  StatusResponseDto<PermitGrantDto>
//     DELETE /permits/{userId}?actionKeyOrRole=…         →  StatusResponseDto (revoke)
//   ActionPermissionsController (api/v1/channels/{channelId}/action-permissions)
//     GET    /action-permissions               →  StatusResponseDto<List<ActionPermissionDto>>
//   RolesController            (api/v1/channels/{channelId}/roles)
//     GET    /roles/effective/me               →  StatusResponseDto<ResolvedAccessDto>  (the caller's own access)
//
// The roles list is a `PaginatedResponse<T>` (a flat `{ data: [...] }`), read with getDirect like the channel
// list. The permits list and the action matrix are `StatusResponseDto<List<T>>` envelopes, read with
// getEnvelope. The role/capability grants return the created grant; the page re-derives its state by reloading,
// so the writes go through putEnvelope/postEnvelope/deleteUnit and the returned body is only used to confirm
// success. `userId` everywhere is the platform User GUID (the backend keys roles on User.Id, not the Twitch id).
interface RolesApi {
    /**
     * The authenticated caller's own resolved access on [channelId] (`GET /roles/effective/me`). The shell calls
     * this on session establish to learn the caller's effective [ManagementRole] (null = a viewer with no Plane-B
     * role) and drive the role-correct sidebar + write affordances. Self-introspection, so the backend gates it by
     * entry only — a viewer can call it to learn they have no management access.
     */
    suspend fun effectiveMe(channelId: String): ApiResult<ResolvedAccess>

    /** The channel's management members — who holds a [ManagementRole] and at what ladder level. */
    suspend fun members(channelId: String): ApiResult<List<ChannelMembership>>

    /** The channel's active (non-expired, non-revoked) per-user permit grants — role grants + capability grants. */
    suspend fun permits(channelId: String): ApiResult<List<PermitGrant>>

    /** The per-action permission matrix — the closed set of action keys a capability can be granted on. */
    suspend fun actionMatrix(channelId: String): ApiResult<List<ActionPermission>>

    /** Assign [userId] the management [role] (permanent membership; the backend re-checks no-escalation). */
    suspend fun assignRole(channelId: String, userId: String, role: ManagementRole): ApiResult<Unit>

    /** Remove [userId]'s management role (demote to no membership). The screen confirms this first. */
    suspend fun removeRole(channelId: String, userId: String): ApiResult<Unit>

    /** Grant [userId] a single capability — the [actionKey] permit (per-user delegation, optional [reason]). */
    suspend fun grantCapability(
        channelId: String,
        userId: String,
        actionKey: String,
        reason: String?,
    ): ApiResult<Unit>

    /**
     * Revoke a permit grant from [userId]: [actionKeyOrRole] selects which grant (a capability's action key or a
     * role token); null revokes all of the user's active grants. The screen confirms this first.
     */
    suspend fun revokePermit(
        channelId: String,
        userId: String,
        actionKeyOrRole: String?,
    ): ApiResult<Unit>
}

class RestRolesApi(private val client: ApiClient) : RolesApi {

    override suspend fun effectiveMe(channelId: String): ApiResult<ResolvedAccess> =
        client.getEnvelope("api/v1/channels/$channelId/roles/effective/me")

    override suspend fun members(channelId: String): ApiResult<List<ChannelMembership>> =
        when (
            val page: ApiResult<PaginatedEnvelope<ChannelMembership>> =
                client.getDirect("api/v1/channels/$channelId/roles?page=1&pageSize=100")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    override suspend fun permits(channelId: String): ApiResult<List<PermitGrant>> =
        client.getEnvelope("api/v1/channels/$channelId/permits")

    override suspend fun actionMatrix(channelId: String): ApiResult<List<ActionPermission>> =
        client.getEnvelope("api/v1/channels/$channelId/action-permissions")

    override suspend fun assignRole(
        channelId: String,
        userId: String,
        role: ManagementRole,
    ): ApiResult<Unit> =
        // The PUT returns the upserted membership; the page reloads to reflect it, so the body is ignored.
        client.putUnit(
            "api/v1/channels/$channelId/roles",
            SetRoleBody(userId = userId, role = role.wire),
        )

    override suspend fun removeRole(channelId: String, userId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/roles/$userId")

    override suspend fun grantCapability(
        channelId: String,
        userId: String,
        actionKey: String,
        reason: String?,
    ): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/permits/capability",
            GrantCapabilityBody(userId = userId, actionKey = actionKey, reason = reason),
        )

    override suspend fun revokePermit(
        channelId: String,
        userId: String,
        actionKeyOrRole: String?,
    ): ApiResult<Unit> {
        val query: String =
            actionKeyOrRole?.let { "?actionKeyOrRole=${it.encodeQuery()}" } ?: ""
        return client.deleteUnit("api/v1/channels/$channelId/permits/$userId$query")
    }
}

/**
 * The Plane-B management ladder (backend `ManagementRole`). The wire form is the enum's integer ordinal (the API
 * registers no string-enum converter, and the OpenAPI declares `ManagementRole` as `{"type": "integer"}`), so
 * each rung pins its [wire] ordinal explicitly — never the ladder [level] (10/20/30/40), which is a separate
 * `LevelValue` field on the membership. `LeadModerator` is the canonical token (the dashboard shell's sidebar
 * gate calls the same rung `SuperMod`; this IAM page speaks the backend vocabulary).
 */
enum class ManagementRole(val wire: Int, val level: Int) {
    Moderator(wire = 0, level = 10),
    LeadModerator(wire = 1, level = 20),
    Editor(wire = 2, level = 30),
    Broadcaster(wire = 3, level = 40);

    companion object {
        /** The ladder, highest first — the order the assign picker and the member sections render in. */
        val byLevelDescending: List<ManagementRole> = entries.sortedByDescending { it.level }

        /** Resolve a wire ordinal to its rung, failing closed to [Moderator] on an unknown value. */
        fun fromWire(wire: Int): ManagementRole = entries.firstOrNull { it.wire == wire } ?: Moderator
    }
}

/**
 * The caller's resolved access on a channel (backend `ResolvedAccessDto`) — the shell's role source. The backend
 * folds the three planes into one [effectiveLevel]; the shell gates on [managementRole], the Plane-B rung that
 * arrives as a **nullable** integer ordinal (the OpenAPI declares `ManagementRole` as `{"type":"integer"}`, and a
 * pure viewer with no management role has it null). The [communityStanding] is the Plane-A rung (an integer
 * ordinal — Everyone/Subscriber/Vip/Artist/Moderator) the **participant** surface unlocks progressively from
 * (a Sub sees a lane a plain viewer doesn't). [permitCapabilities] are the per-user action keys the backend
 * granted, which the participant surface uses to light up capability-gated self-service (e.g. `economy:transfer:write`).
 * `ApiClient`'s JSON ignores any field we don't read here.
 */
@Serializable
data class ResolvedAccess(
    val userId: String,
    val broadcasterId: String,
    val effectiveLevel: Int = 0,
    val communityStanding: Int = 0,
    val communityLevel: Int = 0,
    val managementRole: Int? = null,
    val managementLevel: Int = 0,
    val permitCapabilities: List<String> = emptyList(),
    val winningSource: String = "",
) {
    /** The Plane-B rung the shell gates on, or null when the caller holds no management role (a viewer). */
    val role: ManagementRole?
        get() = managementRole?.let { ManagementRole.fromWire(it) }

    /** The Plane-A community rung the participant surface unlocks from (Everyone/Sub/VIP/Artist/Moderator). */
    val standing: CommunityStanding
        get() = CommunityStanding.fromWire(communityStanding)
}

/**
 * The Plane-A community-standing ladder (backend `CommunityStanding`). The wire form is the enum's integer
 * ordinal (the OpenAPI declares it `{"type":"integer"}`): Everyone=0, Subscriber=1, Vip=2, Artist=3, Moderator=4.
 * The participant surface unlocks progressively up this ladder; [level] is the coarse rank it compares on (a Sub
 * outranks a plain viewer). This is distinct from the Plane-B [ManagementRole] — a viewer with no management role
 * still has a community standing.
 */
enum class CommunityStanding(val wire: Int, val level: Int) {
    Everyone(wire = 0, level = 0),
    Subscriber(wire = 1, level = 20),
    Vip(wire = 2, level = 40),
    Artist(wire = 3, level = 60),
    Moderator(wire = 4, level = 100);

    companion object {
        /** Resolve a wire ordinal to its rung, failing closed to [Everyone] (the least-privileged) on unknown. */
        fun fromWire(wire: Int): CommunityStanding = entries.firstOrNull { it.wire == wire } ?: Everyone
    }
}

/** How a management membership was sourced (backend `MembershipSource`) — an integer ordinal on the wire. */
enum class MembershipSource(val wire: Int) {
    TwitchBadge(0),
    HelixEditors(1),
    BotGrant(2),
    Owner(3);

    companion object {
        fun fromWire(wire: Int): MembershipSource = entries.firstOrNull { it.wire == wire } ?: BotGrant
    }
}

/**
 * One management member (backend `ChannelMembershipDto`). The [role] and [source] arrive as integer ordinals;
 * they are read into ints here (the wire shape) and surfaced as the typed [managementRole] / [membershipSource]
 * the UI renders. [levelValue] is the resolved ladder position; [grantedByUserId] / [lastSyncedAt] are audit.
 */
@Serializable
data class ChannelMembership(
    val id: String,
    val userId: String,
    val username: String? = null,
    val role: Int = 0,
    val levelValue: Int = 0,
    val source: Int = 2,
    val grantedByUserId: String? = null,
    val grantedAt: String = "",
    val lastSyncedAt: String? = null,
) {
    val managementRole: ManagementRole
        get() = ManagementRole.fromWire(role)

    val membershipSource: MembershipSource
        get() = MembershipSource.fromWire(source)
}

/** Whether a permit grants a whole role or a single capability (backend `PermitGrantType`) — wire ordinal. */
enum class PermitGrantType(val wire: Int) {
    Role(0),
    Capability(1);

    companion object {
        fun fromWire(wire: Int): PermitGrantType = entries.firstOrNull { it.wire == wire } ?: Capability
    }
}

/**
 * One active `!permit` grant (backend `PermitGrantDto`): a per-user role grant or a per-user capability grant.
 * [grantType] arrives as an ordinal; [grantedRole] is set only for a role grant and [capabilityActionKey] only
 * for a capability grant. [expiresAt] / [revokedAt] / [reason] are the grant's lifecycle + audit.
 */
@Serializable
data class PermitGrant(
    val id: String,
    val userId: String,
    val username: String? = null,
    val grantType: Int = 1,
    val grantedRole: Int? = null,
    val capabilityActionKey: String? = null,
    val grantedByUserId: String = "",
    val expiresAt: String? = null,
    val revokedAt: String? = null,
    val reason: String? = null,
    val createdAt: String = "",
) {
    val type: PermitGrantType
        get() = PermitGrantType.fromWire(grantType)

    /** The role this grant lifts the user to, when it is a role grant (null for a capability grant). */
    val role: ManagementRole?
        get() = grantedRole?.let { ManagementRole.fromWire(it) }

    /** The single selector that revokes this grant: its action key (capability) or its role token (role grant). */
    val revokeSelector: String?
        get() =
            when (type) {
                PermitGrantType.Capability -> capabilityActionKey
                PermitGrantType.Role -> role?.name
            }
}

/**
 * One row of the per-action permission matrix (backend `ActionPermissionDto`). The page reads it as the closed
 * catalogue of action keys a capability grant may target — only the keys flagged [isGrantableViaPermit] are
 * offered (the backend default-denies the rest). [description] / [effectiveLevel] give the picker its label and
 * the required level it confers.
 */
@Serializable
data class ActionPermission(
    val actionDefinitionId: String,
    val actionKey: String,
    val plane: Int = 0,
    val description: String? = null,
    val defaultLevel: Int = 0,
    val floorLevel: Int = 0,
    val floorTier: Int = 0,
    val isGrantableViaPermit: Boolean = false,
    val overrideLevel: Int? = null,
    val effectiveLevel: Int = 0,
)

/** Request body for the role assignment (backend `RolesController.SetRoleBody`): the user GUID + role ordinal. */
@Serializable
data class SetRoleBody(val userId: String, val role: Int)

/**
 * Request body for a capability permit (backend `PermitsController.GrantCapabilityBody`): the target user, the
 * action key, an optional reason. `expiresAt` is omitted (a permanent capability grant); the backend treats a
 * missing expiry as no expiry, and ApiClient's Json drops nulls so the field simply isn't sent.
 */
@Serializable
data class GrantCapabilityBody(val userId: String, val actionKey: String, val reason: String?)
