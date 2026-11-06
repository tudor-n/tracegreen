import type { EnergySource, NodeStatus, PlausibilityWarning, PortalItem, TransportMode } from '@/api/client'
import { Button } from '@/components/ui/button'
import { AnswerGroup } from '@/components/verify/AnswerGroup'
import { ReconfirmBanner } from '@/components/verify/ResultMessage'
import type { SupplierStrings } from '@/copy/supplier'
import type { Lang, VerifyStrings } from '@/copy/verify'
import { answerSummary, type Draft, isOpenForAnswers } from '@/lib/supplier-state'
import { canSubmit, formatIssueDate, offeredEnergy, offeredModes, reconfirmLines } from '@/lib/verify-state'
import { cn } from '@/lib/utils'

// One delivery on the supplier page: what was invoiced, its status as a colour and a word, and, when it is the open
// one, the two questions of /verify with their pre-selected answers. Invoice text is rendered as text. Sized like the
// phone page: every control at least 56 px high.

const STATUS_TONE: Record<NodeStatus, string> = {
  ESTIMATED: 'border-[var(--status-estimated)] text-[var(--status-estimated)]',
  PARTIAL: 'border-[var(--status-estimated)] text-[var(--status-estimated)]',
  VERIFIED: 'border-[var(--status-verified)] text-[var(--status-verified)]',
}

interface DeliveryCardProps {
  item: PortalItem
  lang: Lang
  copy: SupplierStrings
  questions: VerifyStrings
  /** The open delivery's draft when this card is the open one, else null. */
  draft: Draft | null
  /** True while another delivery's answers are on the way, so no second card opens. */
  busy: boolean
  /** True when the supplier ticked the terms box above the list; nothing is sent without it. */
  termsAccepted: boolean
  warnings: PlausibilityWarning[] | undefined
  onOpen: () => void
  onClose: () => void
  onTransport: (value: TransportMode | null) => void
  onEnergy: (value: EnergySource | null) => void
  onSend: () => void
  onReload: () => void
}

export function DeliveryCard({
  item,
  lang,
  copy,
  questions,
  draft,
  busy,
  termsAccepted,
  warnings,
  onOpen,
  onClose,
  onTransport,
  onEnergy,
  onSend,
  onReload,
}: DeliveryCardProps) {
  const id = `delivery-${item.nodeId}`
  const summary = answerSummary(item, { transport: questions.transport.options, energy: questions.energy.options })
  const sending = draft?.sending ?? false

  return (
    <li data-testid={id} data-status={item.status} className="flex flex-col gap-4 rounded-xl border bg-card p-4">
      {/* The status on its own row, so the invoice text keeps the card's full width at 360 px in either language. */}
      <div className="flex flex-col gap-2">
        <span
          aria-live="polite"
          data-testid={`${id}-status`}
          className={cn(
            'flex items-center gap-1.5 self-start rounded-full border-2 px-2.5 py-1 text-sm font-semibold',
            STATUS_TONE[item.status],
          )}
        >
          <span aria-hidden className="size-2 rounded-full bg-current" />
          {copy.status[item.status]}
        </span>
        <p className="text-base font-semibold">
          {item.invoiceNumber} · {formatIssueDate(item.issueDate, lang)}
        </p>
        <p className="text-base break-words">{item.itemDescription}</p>
        <p className="text-sm text-muted-foreground">
          {item.origin} → {item.dest}
        </p>
      </div>

      {summary !== '' && draft === null && <p className="text-base text-muted-foreground">{summary}</p>}

      {warnings !== undefined && warnings.length > 0 && (
        <ReconfirmBanner title={questions.reconfirm.title} lines={reconfirmLines(warnings, questions.reconfirm.warnings)} />
      )}

      {draft === null && isOpenForAnswers(item) && (
        <Button
          variant="outline"
          data-testid={`${id}-open`}
          className="h-14 w-full rounded-xl text-lg"
          disabled={busy}
          onClick={onOpen}
        >
          {copy.open}
        </Button>
      )}

      {draft !== null && (
        <>
          <AnswerGroup
            question={questions.transport.question}
            options={offeredModes(item).map((mode) => ({ value: mode, label: questions.transport.options[mode] }))}
            selected={draft.answers.transport}
            skipLabel={questions.skip}
            testId={`${id}-transport`}
            disabled={sending}
            onChange={onTransport}
          />
          <AnswerGroup
            question={questions.energy.question}
            options={offeredEnergy(item).map((source) => ({ value: source, label: questions.energy.options[source] }))}
            selected={draft.answers.energy}
            skipLabel={questions.skip}
            testId={`${id}-energy`}
            disabled={sending}
            onChange={onEnergy}
          />

          {!canSubmit(draft.answers) ? (
            <p role="status" className="text-center text-base text-muted-foreground">
              {questions.answerOne}
            </p>
          ) : (
            !termsAccepted && (
              <p role="status" className="text-center text-base text-muted-foreground">
                {questions.terms.tickToSubmit}
              </p>
            )
          )}
          {draft.failure !== null && (
            <p role="alert" data-testid={`${id}-failure`} className="text-center text-base text-[var(--status-estimated)]">
              {copy.deliveryFailures[draft.failure]}
            </p>
          )}

          <div className="flex flex-col gap-2">
            {draft.failure === 'changed' ? (
              <Button className="h-14 w-full rounded-xl text-lg" onClick={onReload}>
                {copy.reload}
              </Button>
            ) : (
              <Button
                data-testid={`${id}-confirm`}
                className="h-14 w-full rounded-xl text-lg"
                disabled={sending || !termsAccepted || !canSubmit(draft.answers)}
                onClick={onSend}
              >
                {sending ? copy.sending : copy.send}
              </Button>
            )}
            <Button variant="ghost" className="h-12 w-full rounded-xl text-base" disabled={sending} onClick={onClose}>
              {copy.close}
            </Button>
          </div>
        </>
      )}
    </li>
  )
}
