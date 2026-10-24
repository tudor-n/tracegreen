import { Handle, type NodeProps, Position } from '@xyflow/react'
import { memo } from 'react'
import { nodeCopy } from '@/copy/dashboard'
import type { BuyerRfNode } from '@/store'

// The buyer every supplier edge flows into, drawn to fill NODE_SIZE.buyer.

function BuyerNodeView({ data }: NodeProps<BuyerRfNode>) {
  const { buyer } = data
  return (
    <div
      data-testid="node-buyer"
      className="flex size-full flex-col justify-center overflow-hidden rounded-xl border-2 border-foreground/70 bg-card px-3 py-2 shadow-sm"
    >
      <p className="text-[10px] font-medium tracking-wide text-muted-foreground uppercase">{nodeCopy.buyer}</p>
      <p className="line-clamp-2 text-sm leading-tight font-semibold" title={buyer.name}>
        {buyer.name}
      </p>
      <p className="truncate text-xs text-muted-foreground">
        {buyer.city}, {buyer.country}
      </p>
      <Handle type="target" position={Position.Left} isConnectable={false} className="opacity-0" />
    </div>
  )
}

export const BuyerNode = memo(BuyerNodeView)
