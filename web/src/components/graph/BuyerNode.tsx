import { Handle, type NodeProps, Position } from '@xyflow/react'
import { memo } from 'react'
import { nodeCopy } from '@/copy/dashboard'
import type { BuyerRfNode } from '@/store'

// The buyer every supplier edge flows into, drawn to fill NODE_SIZE.buyer. It glows brighter green as more of the chain
// is supplier-confirmed (`--g`, set on the dashboard window from the API's share; index.css).

function BuyerNodeView({ data }: NodeProps<BuyerRfNode>) {
  const { buyer } = data
  return (
    <div
      data-testid="node-buyer"
      className="buyer-node flex size-full flex-col justify-center overflow-hidden rounded-[var(--r-card)] px-4 py-3"
    >
      <p className="text-[11px] font-semibold text-green-ink">{nodeCopy.buyer}</p>
      <p className="line-clamp-2 font-heading text-[15px] leading-tight font-semibold tracking-[-0.01em]" title={buyer.name}>
        {buyer.name}
      </p>
      <p className="truncate text-xs text-mute">
        {buyer.city}, {buyer.country}
      </p>
      <Handle type="target" position={Position.Left} isConnectable={false} className="opacity-0" />
    </div>
  )
}

export const BuyerNode = memo(BuyerNodeView)
