import { useId, useState } from 'react'
import type { CategoryOption, NodeView } from '@/api/client'
import { GroupRow, PageMessage } from '@/components/shell/Page'
import { Button } from '@/components/ui/button'
import { classificationCopy, drawerCopy } from '@/copy/dashboard'
import { correctCategory, fetchCategories } from '@/lib/dashboard-actions'

/**
 * The node's category in the Details tab, with a quiet "Change" (F6). The select appears only after it is pressed; saving
 * asks the API to recompute the node, and the new figures arrive through node.updated like every other change.
 */
export function CategoryRow({ node }: { node: NodeView }) {
  const selectId = useId()
  const current = node.classification.categoryId
  const [editing, setEditing] = useState(false)
  const [options, setOptions] = useState<CategoryOption[] | null>(null)
  const [choice, setChoice] = useState(current)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const open = async () => {
    setEditing(true)
    setError(null)
    setChoice(current)
    if (options !== null) return
    const answer = await fetchCategories()
    if (answer.ok) setOptions(answer.data)
    else setError(answer.message)
  }

  const save = async () => {
    setSaving(true)
    setError(null)
    const answer = await correctCategory(node.id, choice)
    setSaving(false)
    if (answer.ok) setEditing(false)
    else setError(answer.message)
  }

  if (!editing) {
    return (
      <GroupRow label={drawerCopy.classification}>
        <span className="inline-flex flex-wrap items-baseline justify-end gap-x-2">
          {node.classification.categoryLabel}
          <button
            type="button"
            data-testid="drawer-category-change"
            aria-label={classificationCopy.changeLabel}
            onClick={() => void open()}
            className="rounded-sm text-[13px] font-medium text-primary underline-offset-4 outline-none hover:underline focus-visible:ring-2 focus-visible:ring-ring"
          >
            {classificationCopy.change}
          </button>
        </span>
      </GroupRow>
    )
  }

  const unchanged = choice === current && node.classification.source === 'correction'
  return (
    <div data-testid="drawer-category" className="flex flex-col gap-2 py-3 text-sm">
      <label htmlFor={selectId} className="text-ink-2">
        {drawerCopy.classification}
      </label>
      {options === null ? (
        error === null && <PageMessage busy>{classificationCopy.loading}</PageMessage>
      ) : (
        <select
          id={selectId}
          data-testid="drawer-category-select"
          value={choice}
          disabled={saving}
          onChange={(event) => setChoice(event.target.value)}
          className="h-9 rounded-lg bg-card px-2 text-sm font-medium outline-none focus-visible:ring-2 focus-visible:ring-ring"
        >
          {options.map((option) => (
            <option key={option.id} value={option.id}>
              {option.label}
            </option>
          ))}
        </select>
      )}
      <p className="text-[11px] text-muted-foreground">{classificationCopy.note}</p>
      {error !== null && (
        <p role="alert" className="text-[13px] text-destructive">
          {error}
        </p>
      )}
      <div className="flex justify-end gap-2">
        <Button variant="ghost" size="sm" onClick={() => setEditing(false)} disabled={saving}>
          {classificationCopy.cancel}
        </Button>
        <Button size="sm" data-testid="drawer-category-save" onClick={() => void save()} disabled={saving || options === null || unchanged}>
          {saving ? classificationCopy.saving : classificationCopy.save}
        </Button>
      </div>
    </div>
  )
}
