import { defineComponent, Fragment, h, Text, type VNode } from 'vue'

// Text nodes do not count in CSS :first-child/:last-child. Give labels an element so an
// icon beside a plain translated string gets the same geometry as one beside a span.
function labels(nodes: (VNode | string | number)[]): (VNode | string | number)[] {
  return nodes.map((node) => {
    if (typeof node === 'string' || typeof node === 'number') {
      return String(node).trim() ? h('span', String(node)) : node
    }
    if (node.type === Text && String(node.children).trim()) {
      return h('span', String(node.children))
    }
    if (node.type === Fragment && Array.isArray(node.children)) {
      return h(Fragment, labels(node.children as VNode[]))
    }
    return node
  })
}

export default defineComponent({
  setup(_, { slots }) {
    return () => labels(slots.default?.() ?? [])
  },
})
