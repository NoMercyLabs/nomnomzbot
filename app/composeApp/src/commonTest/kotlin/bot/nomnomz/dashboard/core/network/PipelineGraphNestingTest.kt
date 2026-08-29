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

import kotlinx.serialization.json.Json
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

// S046-branching-prereq: the backend's wire graph now carries tree-nesting fields
// (id/parent_step_id/branch/block_kind/block_config/order) alongside the flat action/condition
// shape, so an `if`-block pipeline can round-trip through the editor's model. These tests prove
// two things: (1) a literal nested graph payload decodes into PipelineGraph/PipelineStep with every
// new field intact, and (2) today's flat payload (none of those keys present) still decodes exactly
// as it did before this slice — a regression guard for every existing pipeline the editor renders.
class PipelineGraphNestingTest {

    @Test
    fun nested_if_then_else_graph_decodes_with_parent_branch_block_kind_and_order_intact() {
        val json = """
            {
              "steps": [
                {
                  "id": "11111111-1111-1111-1111-111111111111",
                  "parent_step_id": null,
                  "branch": null,
                  "block_kind": "if",
                  "block_config": { "ConditionRootId": "00000000-0000-0000-0000-000000000000" },
                  "order": 0,
                  "action": { "type": "block" }
                },
                {
                  "id": "22222222-2222-2222-2222-222222222222",
                  "parent_step_id": "11111111-1111-1111-1111-111111111111",
                  "branch": "then",
                  "block_kind": null,
                  "order": 0,
                  "action": { "type": "send_message", "message": "then-branch" }
                },
                {
                  "id": "33333333-3333-3333-3333-333333333333",
                  "parent_step_id": "11111111-1111-1111-1111-111111111111",
                  "branch": "else",
                  "order": 0,
                  "action": { "type": "shoutout" }
                }
              ]
            }
        """.trimIndent()

        val graph: PipelineGraph = PipelineGraph.fromJson(Json.parseToJsonElement(json))

        assertEquals(3, graph.steps.size)

        val ifStep: PipelineStep = graph.steps[0]
        assertEquals("11111111-1111-1111-1111-111111111111", ifStep.id)
        assertNull(ifStep.parentStepId)
        assertNull(ifStep.branch)
        assertEquals("if", ifStep.blockKind)
        assertEquals(0, ifStep.order)
        val blockConfig = ifStep.blockConfig as? kotlinx.serialization.json.JsonObject
        assertEquals(
            "00000000-0000-0000-0000-000000000000",
            (blockConfig?.get("ConditionRootId") as? kotlinx.serialization.json.JsonPrimitive)?.content,
        )

        val thenStep: PipelineStep = graph.steps[1]
        assertEquals("11111111-1111-1111-1111-111111111111", thenStep.parentStepId)
        assertEquals("then", thenStep.branch)
        assertNull(thenStep.blockKind)
        assertEquals("send_message", thenStep.action.type)

        val elseStep: PipelineStep = graph.steps[2]
        assertEquals("11111111-1111-1111-1111-111111111111", elseStep.parentStepId)
        assertEquals("else", elseStep.branch)
        assertEquals("shoutout", elseStep.action.type)

        // Re-encode: the tree-nesting fields must survive toJson() too (the editor's save path).
        val reencoded: PipelineGraph = PipelineGraph.fromJson(graph.toJson())
        assertEquals(graph.steps.map { it.id }, reencoded.steps.map { it.id })
        assertEquals(graph.steps.map { it.parentStepId }, reencoded.steps.map { it.parentStepId })
        assertEquals(graph.steps.map { it.branch }, reencoded.steps.map { it.branch })
        assertEquals(graph.steps.map { it.blockKind }, reencoded.steps.map { it.blockKind })
    }

    @Test
    fun flat_payload_with_no_nesting_keys_decodes_exactly_as_before_this_slice() {
        val json = """
            {
              "steps": [
                { "action": { "type": "send_message", "message": "hi" } },
                {
                  "action": { "type": "timeout_user", "seconds": 30 },
                  "condition": { "type": "user_role", "operator": "eq", "left": "role", "right": "moderator", "negate": false },
                  "stop_on_match": true
                }
              ]
            }
        """.trimIndent()

        val graph: PipelineGraph = PipelineGraph.fromJson(Json.parseToJsonElement(json))

        assertEquals(2, graph.steps.size)

        val first: PipelineStep = graph.steps[0]
        assertEquals("send_message", first.action.type)
        assertNull(first.id)
        assertNull(first.parentStepId)
        assertNull(first.branch)
        assertNull(first.blockKind)
        assertNull(first.blockConfig)
        assertNull(first.order)
        assertEquals(false, first.stopOnMatch)

        val second: PipelineStep = graph.steps[1]
        assertEquals("timeout_user", second.action.type)
        assertEquals("user_role", second.condition?.type)
        assertEquals(true, second.stopOnMatch)
        assertNull(second.parentStepId)
        assertNull(second.blockKind)

        // Re-encoding a flat step must not introduce any of the new keys — proves toJson() is
        // additive-only and a flat pipeline's saved wire shape is unchanged.
        val reencodedJson = graph.toJson().toString()
        assert(!reencodedJson.contains("parent_step_id")) {
            "flat step must not encode parent_step_id: $reencodedJson"
        }
        assert(!reencodedJson.contains("block_kind")) {
            "flat step must not encode block_kind: $reencodedJson"
        }
    }
}
