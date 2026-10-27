import { Handle, type NodeProps, Position } from '@xyflow/react'
import { memo } from 'react'
import { sidebarCopy } from '@/copy/dashboard'
import { CO2E_UNIT, type BuyerRfNode, formatTonnes, useGraphStore } from '@/store'

// The buyer every supplier edge flows into: a dark card drawn to fill NODE_SIZE.buyer, with the API's total. It glows
// brighter green as more of the chain is supplier-confirmed (`--g`, set on the dashboard window from the API's share;
// index.css).

function BuyerNodeView({ data }: NodeProps<BuyerRfNode>) {
  const { buyer } = data
  const totalKg = useGraphStore((s) => s.kpis?.totalKg ?? null)
  return (
    <div
      data-testid="node-buyer"
      className="buyer-node flex size-full flex-col justify-center gap-0.5 overflow-hidden rounded-[var(--r-tile)] px-4 py-3"
    >
      <p className="line-clamp-2 text-[13px] leading-tight font-semibold" title={buyer.name}>
        {buyer.name}
      </p>
      <p className="truncate text-xs text-white/72">{sidebarCopy.buyerLine(buyer.city)}</p>
      {totalKg !== null && (
        <p className="text-[13px] font-semibold">
          {formatTonnes(totalKg)} {CO2E_UNIT}
        </p>
      )}
      <Handle type="target" position={Position.Left} isConnectable={false} className="opacity-0" />
    </div>
  )
}

export const BuyerNode = memo(BuyerNodeView)
