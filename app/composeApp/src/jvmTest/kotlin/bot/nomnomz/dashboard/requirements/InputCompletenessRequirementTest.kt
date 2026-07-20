// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.requirements

import bot.nomnomz.dashboard.core.network.BanUserBody
import bot.nomnomz.dashboard.core.network.CreateCatalogItemBody
import bot.nomnomz.dashboard.core.network.CreateChatTriggerBody
import bot.nomnomz.dashboard.core.network.CreateCommandBody
import bot.nomnomz.dashboard.core.network.CreateModerationRuleBody
import bot.nomnomz.dashboard.core.network.CreatePickListBody
import bot.nomnomz.dashboard.core.network.CreateRewardBody
import bot.nomnomz.dashboard.core.network.CreateTimerRequest
import bot.nomnomz.dashboard.core.network.CreateWidgetBody
import bot.nomnomz.dashboard.core.network.UpdateCatalogItemBody
import bot.nomnomz.dashboard.core.network.UpdateRewardBody
import bot.nomnomz.dashboard.core.network.UpdateRuleBody
import bot.nomnomz.dashboard.core.network.UpsertEscalationPolicyBody
import bot.nomnomz.dashboard.core.network.UpsertGiveawayBody
import kotlin.test.Test
import kotlin.test.fail
import kotlinx.serialization.KSerializer
import kotlinx.serialization.descriptors.elementNames

// REQUIREMENT: every input the backend ACCEPTS on a request body must be present on the dashboard's body DTO,
// so the operator can actually edit it. An input the API takes but the body omits is a setting the user can
// never reach through the dashboard — the create/edit form simply cannot express it.
//
// Same inversion as ApiContractTest, applied to request bodies: `backendInputs - bodyFields == ∅`. A non-empty
// difference is the "uneditable input" backlog — e.g. a create dialog that collects name+cost but silently
// hard-codes everything else the endpoint supports.
//
// Note: some request schemas legitimately expose more than the dashboard maps (an input bound server-side, or a
// deliberately narrow "toggle only" body). Those still count against completeness on purpose — the failure list
// is the authoritative "inputs the dashboard cannot set yet" inventory, to be triaged rather than assumed fine.
class InputCompletenessRequirementTest {

    // (Kotlin request-body serializer, backend request schema name, human label).
    private val inputContracts: List<Triple<KSerializer<*>, String, String>> =
        listOf(
            Triple(CreateCatalogItemBody.serializer(), "CreateCatalogItemRequest", "create store item"),
            Triple(UpdateCatalogItemBody.serializer(), "UpdateCatalogItemRequest", "edit store item"),
            Triple(CreateRewardBody.serializer(), "CreateRewardRequest", "create channel-point reward"),
            Triple(UpdateRewardBody.serializer(), "UpdateRewardRequest", "edit channel-point reward"),
            Triple(CreateModerationRuleBody.serializer(), "CreateModerationRuleRequest", "create moderation rule"),
            Triple(UpdateRuleBody.serializer(), "UpdateModerationRuleRequest", "edit moderation rule"),
            Triple(UpsertGiveawayBody.serializer(), "UpsertGiveawayRequest", "create/edit giveaway"),
            Triple(CreateCommandBody.serializer(), "CreateCommandDto", "create command"),
            Triple(CreateTimerRequest.serializer(), "CreateTimerDto", "create timer"),
            Triple(UpsertEscalationPolicyBody.serializer(), "UpsertEscalationPolicyRequest", "escalation policy"),
            Triple(CreateWidgetBody.serializer(), "CreateWidgetRequest", "create widget"),
            Triple(BanUserBody.serializer(), "BanUserRequest", "ban a viewer"),
            Triple(CreatePickListBody.serializer(), "CreatePickListRequest", "create pick list"),
            Triple(CreateChatTriggerBody.serializer(), "CreateChatTriggerRequest", "create chat trigger"),
        )

    @Test
    fun every_request_body_carries_every_input_the_backend_accepts() {
        val problems: MutableList<String> = mutableListOf()
        var complete = 0

        for ((serializer, schemaName, label) in inputContracts) {
            val backend: Set<String>? = BackendContract.backendProperties(schemaName)
            if (backend == null) {
                problems += "$schemaName: no such schema in the OpenAPI document (fix the mapping)"
                continue
            }
            val bodyFields: Set<String> = serializer.descriptor.elementNames.toSet()
            val missing: Set<String> = backend - bodyFields
            if (missing.isEmpty()) {
                complete++
            } else {
                problems +=
                    "$schemaName ($label): body omits ${missing.sorted()} — the backend accepts these inputs " +
                        "but the dashboard form cannot set them (body carries ${bodyFields.sorted()})"
            }
        }

        println("[input-completeness] $complete/${inputContracts.size} request bodies carry every backend input")

        if (problems.isNotEmpty()) {
            fail(
                "The dashboard cannot set inputs the backend accepts (a complete reflection must expose every " +
                    "editable input):\n" + problems.joinToString("\n") { "  • $it" }
            )
        }
    }
}
