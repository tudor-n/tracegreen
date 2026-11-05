import '@xyflow/react/dist/style.css'
import {
  Background,
  Controls,
  type NodeChange,
  type NodeTypes,
  ReactFlow,
  ReactFlowProvider,
  useReactFlow,
} from '@xyflow/react'
import { useCallback, useEffect } from 'react'
import { canvasCopy } from '@/copy/dashboard'
import { type GraphRfNode, layoutKey, selectGraphView, useGraphStore } from '@/store'
import { BuyerNode } from './BuyerNode'
import { GhostNode } from './GhostNode'
import { SupplierNode } from './SupplierNode'

// The supplier graph. Controlled: nodes and edges come from the memoised store selector, positions from the dagre
// layout. Only selection goes back, as the store's `select` action; React Flow's node objects, measurements and
// positions are never stored (roadmap §8.4).

const nodeTypes: NodeTypes = { supplier: SupplierNode, buyer: BuyerNode, ghost: GhostNode }

const FIT_VIEW = { padding: 0.15, maxZoom: 1.25, duration: 300 }

export function GraphCanvas() {
  return (
    <ReactFlowProvider>
      <Canvas />
    </ReactFlowProvider>
  )
}

function Canvas() {
  const { nodes, edges } = useGraphStore(selectGraphView)
  const select = useGraphStore((s) => s.select)
  const nodeSet = useGraphStore((s) => layoutKey(Object.keys(s.nodes)))
  const ghostOf = useGraphStore((s) => s.scenarioPreview?.nodeId ?? null)
  const { fitView } = useReactFlow()

  // A node arriving or leaving moves the whole column, and a ghost appears left of it, so frame the graph again: the
  // new node may be off screen. Picking another scenario for the same supplier keeps the frame.
  useEffect(() => {
    void fitView(FIT_VIEW)
  }, [fitView, nodeSet, ghostOf])

  // Clicks, Enter on a focused node, Escape and clicks on the pane all arrive as select changes.
  const onNodesChange = useCallback(
    (changes: NodeChange<GraphRfNode>[]) => {
      let selection: string | null | undefined
      for (const change of changes) {
        if (change.type !== 'select') continue
        if (change.selected) selection = change.id
        else if (selection === undefined) selection = null
      }
      if (selection !== undefined) select(selection)
    },
    [select],
  )

  return (
    <ReactFlow
      aria-label={canvasCopy.label}
      className="[--xy-edge-stroke:color-mix(in_oklab,var(--muted-foreground)_55%,transparent)]"
      nodes={nodes}
      edges={edges}
      nodeTypes={nodeTypes}
      onNodesChange={onNodesChange}
      nodesDraggable={false}
      nodesConnectable={false}
      edgesFocusable={false}
      fitView
      fitViewOptions={FIT_VIEW}
      minZoom={0.2}
      maxZoom={2}
    >
      <Background color="var(--border)" gap={20} />
      <Controls showInteractive={false} />
    </ReactFlow>
  )
}
