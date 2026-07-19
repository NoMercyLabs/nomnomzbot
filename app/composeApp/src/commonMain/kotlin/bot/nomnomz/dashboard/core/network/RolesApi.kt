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

    /**
     * Search the platform's users by login/display name for the viewer picker (`GET /api/v1/users?query=…`).
     * This is what lets the assign-role and grant-permit flows reach a viewer who is NOT already a member — the
     * result's [UserSearchResult.id] is the platform User GUID the writes key on. Floored at `community:read` on
     * the resolved tenant (the operator's active channel), so a Broadcaster driving this page always clears it.
     */
    suspend fun searchViewers(query: String): ApiResult<List<UserSearchResult>>

    /** Set an override [level] on the action [actionKey] — overrides the default floor without removing it. */
    suspend fun setOverride(channelId: String, actionKey: String, level: Int): ApiResult<Unit>

    /** Reset the override on [actionKey], restoring the action's built-in default floor level. */
    suspend fun resetOverride(channelId: String, actionKey: String): ApiResult<Unit>

    /** Assign [userId] the management [role] (permanent membership; the backend re-checks no-escalation). */
    suspend fun assignRole(channelId: String, userId: String, role: ManagementRole): ApiResult<Unit>

    /** Remove [userId]'s management role (demote to no membership). The screen confirms this first. */
    suspend fun removeRole(channelId: String, userId: String): ApiResult<Unit>

    /**
     * Grant [userId] a whole management [role] via a permit (`POST /permits/role`) — a delegated, optionally
     * expiring lift to the role rather than a permanent membership (that is [assignRole]). [expiresAt] is an ISO-8601
     * instant (null = no expiry); [reason] is audit. Reaches any user id, so it is the grant-a-role-to-a-viewer path.
     */
    suspend fun grantRole(
        channelId: String,
        userId: String,
        role: ManagementRole,
        expiresAt: String?,
        reason: String?,
    ): ApiResult<Unit>

    /**
     * Grant [userId] a single capability — the [actionKey] permit (per-user delegation). [expiresAt] is an ISO-8601
     * instant (null = permanent); [reason] is audit. Reaches any user id, so it is the grant-a-capability-to-a-viewer
     * path.
     */
    suspend fun grantCapability(
        channelId: String,
        userId: String,
        actionKey: String,
        expiresAt: String?,
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

    override suspend fun searchViewers(query: String): ApiResult<List<UserSearchResult>> =
        // A flat `{ data: [...] }` PaginatedResponse (like the members list), so getDirect. The tenant the search
        // is authorized against comes from the ApiClient's X-Channel-Id (the operator's active channel).
        when (
            val page: ApiResult<PaginatedEnvelope<UserSearchResult>> =
                client.getDirect(
                    "api/v1/users?query=${query.encodeQuery()}&page=1&pageSize=20"
                )
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    override suspend fun setOverride(channelId: String, actionKey: String, level: Int): ApiResult<Unit> =
        client.putUnit(
            "api/v1/channels/$channelId/action-permissions/${actionKey.encodeQuery()}",
            SetOverrideBody(level = level),
        )

    override suspend fun resetOverride(channelId: String, actionKey: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/action-permissions/${actionKey.encodeQuery()}")

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

    override suspend fun grantRole(
        channelId: String,
        userId: String,
        role: ManagementRole,
        expiresAt: String?,
        reason: String?,
    ): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/permits/role",
            GrantRoleBody(
                userId = userId,
                role = role.wire,
                expiresAt = expiresAt,
                reason = reason,
            ),
        )

    override suspend fun grantCapability(
        channelId: String,
        userId: String,
        actionKey: String,
        expiresAt: String?,
        reason: String?,
    ): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/permits/capability",
            GrantCapabilityBody(
                userId = userId,
                actionKey = actionKey,
                expiresAt = expiresAt,
                reason = reason,
            ),
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
 * [heldActionKeys] is the broader, UI-facing set the shell gates VISIBILITY on: EVERY action key the caller
 * actually CLEARS on this channel — their effective level meets the action's channel-effective required level
 * (which FOLDS IN the broadcaster's per-action override, unlike the per-plane [effectiveLevel]/[permitCapabilities]
 * fields), OR they hold a direct per-user capability grant for it. It is what lets a broadcaster-lowered page
 * surface to a VIP/Sub. `ApiClient`'s JSON ignores any field we don't read here.
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
    val heldActionKeys: List<String> = emptyList(),
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

/**
 * One user matched by the viewer picker (backend `UserSearchResult`, surfaced through the `UserDto`-typed
 * `GET /api/v1/users` contract). [id] is the platform User GUID the role/permit writes key on; [displayName] /
 * [username] label the row and [profileImageUrl] gives it an avatar. The picker reads only this subset of the
 * declared `UserDto` (ApiClient's Json ignores the extra `email`/`createdAt`/`lastLoginAt` fields).
 */
@Serializable
data class UserSearchResult(
    val id: String,
    val username: String = "",
    val displayName: String = "",
    val profileImageUrl: String? = null,
)

/** Request body for the role assignment (backend `RolesController.SetRoleBody`): the user GUID + role ordinal. */
@Serializable
data class SetRoleBody(val userId: String, val role: Int)

/**
 * Request body for a role permit (backend `PermitsController.GrantRoleBody`): the target user GUID, the role
 * ordinal, an optional ISO-8601 [expiresAt] (null = no expiry — ApiClient's Json drops the null so it isn't sent),
 * and an optional [reason].
 */
@Serializable
data class GrantRoleBody(
    val userId: String,
    val role: Int,
    val expiresAt: String?,
    val reason: String?,
)

/** Request body for an action-permission level override (backend `ActionPermissionsController.SetOverrideBody`). */
@Serializable
data class SetOverrideBody(val level: Int)

/**
 * Request body for a capability permit (backend `PermitsController.GrantCapabilityBody`): the target user, the
 * action key, an optional ISO-8601 [expiresAt] (null = no expiry), and an optional [reason]. ApiClient's Json
 * drops nulls, so a permanent grant simply omits `expiresAt`.
 */
@Serializable
data class GrantCapabilityBody(
    val userId: String,
    val actionKey: String,
    val expiresAt: String?,
    val reason: String?,
)
