import { useMemo } from 'react'
import { useSearchParams } from 'react-router'
import type { NodeView } from '@/api/client'
import { PageHeader, PageMessage } from '@/components/shell/Page'
import { CheckFilter } from '@/components/suppliers/CheckFilter'
import { SupplierPanel } from '@/components/suppliers/SupplierPanel'
import { SupplierTree } from '@/components/suppliers/SupplierTree'
import { answersCopy, canvasCopy, reviewCopy, suppliersCopy } from '@/copy/dashboard'
import { CHECK_FILTER, categoriesToCheck } from '@/lib/review-display'
import { ANSWERS_FILTER, answersToCheck } from '@/lib/trust-display'
import { byCarbonDesc, formatTonnes, hotspotIds, selectLoaded, selectSelectedNode, useGraphStore } from '@/store'
import { useShell } from '@/hooks/useShell'

// "Who?": the supplier tree, and beside it the side panel for the selected supplier. Rows are ordered by the API's
// carbonKg, largest first; the hotspot is the store's derived overlay, never a status.

/** Largest emitter at the top; ties keep a stable order by id. */
function byEmissions(nodes: Record<string, NodeView>): NodeView[] {
  return Object.values(nodes).sort(byCarbonDesc)
}

export default function Suppliers() {
  const { connection } = useShell()
  const loaded = useGraphStore(selectLoaded)
  const nodeMap = useGraphStore((s) => s.nodes)
  const buyer = useGraphStore((s) => s.buyer)
  const kpis = useGraphStore((s) => s.kpis)
  const selected = useGraphStore(selectSelectedNode)
  const open = useGraphStore((s) => s.openPanels.drawer) && selected !== null
  // The chat covers the side panel; the panel stays mounted underneath, so closing the chat shows it as it was.
  const chatOpen = useGraphStore((s) => s.openPanels.chat)
  const preview = useGraphStore((s) => s.scenarioPreview)
  const select = useGraphStore((s) => s.select)
  const nodes = useMemo(() => byEmissions(nodeMap), [nodeMap])
  const hotspots = useMemo(() => hotspotIds(nodes), [nodes])
  // The Overview's counters open this list filtered to the categories to check (F6) or to the supplier answers to check,
  // the most kilograms at risk first (F7); the hotspots stay those of all nodes.
  const [params] = useSearchParams()
  const filter = params.get(CHECK_FILTER.param)
  const kind = filter === CHECK_FILTER.value ? 'category' : filter === ANSWERS_FILTER.value ? 'answers' : null
  const checking = kind !== null
  const listed = useMemo(
    () => (kind === 'category' ? categoriesToCheck(nodes) : kind === 'answers' ? answersToCheck(nodes) : nodes),
    [kind, nodes],
  )

  let body
  if (!loaded) {
    body =
      connection === 'offline' ? (
        <PageMessage tone="error">{canvasCopy.offline}</PageMessage>
      ) : (
        <PageMessage busy>{canvasCopy.loading}</PageMessage>
      )
  } else if (nodes.length === 0) {
    body = <PageMessage>{canvasCopy.empty}</PageMessage>
  } else if (listed.length === 0) {
    body = (
      <>
        <PageMessage>{kind === 'answers' ? answersCopy.noneToCheck : reviewCopy.noneToCheck}</PageMessage>
        <CheckFilter kind={kind ?? 'category'} />
      </>
    )
  } else {
    body = (
      <>
        {checking && <CheckFilter kind={kind} />}
        <SupplierTree
          nodes={listed}
          hotspots={hotspots}
          buyer={buyer}
          total={kpis === null ? null : formatTonnes(kpis.totalKg)}
          selectedId={open ? selected.id : null}
          preview={preview}
          onSelect={select}
        />
        {!open && <p className="px-1 text-[13px] text-muted-foreground">{suppliersCopy.selectHint}</p>}
      </>
    )
  }

  return (
    <div className="flex min-w-0 flex-1 flex-col lg:flex-row">
      <main data-testid="graph" className="min-w-0 flex-1 px-5 py-8 md:px-8 md:py-11">
        <div className="flex flex-col gap-6">
          <PageHeader title={suppliersCopy.title} subtitle={suppliersCopy.subtitle} />
          {body}
        </div>
      </main>
      {open && (
        <div className={chatOpen ? 'hidden' : 'contents'}>
          <SupplierPanel node={selected} hotspot={hotspots.has(selected.id)} />
        </div>
      )}
    </div>
  )
}
