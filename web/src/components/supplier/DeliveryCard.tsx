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
// phone page: every control at least 52 px high. In the v2 grouped style the open delivery's questions sit on a grey
// inset, so their white lists read as lists and not as more card.

const STATUS_TONE: Record<NodeStatus, string> = {
  ESTIMATED: 'bg-estimated-tint text-estimated-ink',
  PARTIAL: 'bg-estimated-tint text-estimated-ink',
  VERIFIED: 'bg-verified-tint text-verified-ink',
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
    <li data-testid={id} data-status={item.status} className="flex flex-col gap-4 rounded-[18px] bg-card p-5 shadow-card transition-shadow duration-300 data-[status=VERIFIED]:shadow-[var(--shadow-card),0_0_0_1px_rgb(18_129_79/0.18)]">
      {/* The status on its own row, so the invoice text keeps the card's full width at 360 px in either language. */}
      <div className="flex flex-col gap-2">
        <span
          aria-live="polite"
          data-testid={`${id}-status`}
          className={cn(
            'inline-flex h-7 items-center gap-1.5 self-start rounded-full px-3 text-[13px] font-semibold transition-colors duration-300',
            STATUS_TONE[item.status],
          )}
        >
          <span aria-hidden data-status={item.status} className="status-dot size-2 rounded-full" />
          {copy.status[item.status]}
        </span>
        <p className="mt-1 text-[17px] font-semibold tracking-[-0.015em]">
          {item.invoiceNumber} · {formatIssueDate(item.issueDate, lang)}
        </p>
        <p className="text-[15px] leading-relaxed break-words">{item.itemDescription}</p>
        <p className="text-[13px] text-muted-foreground">
          {item.origin} → {item.dest}
        </p>
      </div>

      {summary !== '' && draft === null && <p className="text-[15px] text-ink-2">{summary}</p>}

      {warnings !== undefined && warnings.length > 0 && (
        <ReconfirmBanner title={questions.reconfirm.title} lines={reconfirmLines(warnings, questions.reconfirm.warnings)} />
      )}

      {draft === null && isOpenForAnswers(item) && (
        <Button
          variant="outline"
          data-testid={`${id}-open`}
          className="h-[52px] w-full text-[17px]"
          disabled={busy}
          onClick={onOpen}
        >
          {copy.open}
        </Button>
      )}

      {draft !== null && (
        <div className="-mx-2 flex flex-col gap-4 rounded-[14px] bg-grouped p-3 motion-safe:animate-in motion-safe:fade-in-0 motion-safe:slide-in-from-top-1 motion-safe:duration-300">
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
            <p role="status" className="text-center text-[15px] text-ink-2">
              {questions.answerOne}
            </p>
          ) : (
            !termsAccepted && (
              <p role="status" className="text-center text-[15px] text-ink-2">
                {questions.terms.tickToSubmit}
              </p>
            )
          )}
          {draft.failure !== null && (
            <p role="alert" data-testid={`${id}-failure`} className="rounded-lg bg-estimated-tint px-3 py-2 text-center text-[15px] text-estimated-ink">
              {copy.deliveryFailures[draft.failure]}
            </p>
          )}

          <div className="flex flex-col gap-2">
            {draft.failure === 'changed' ? (
              <Button className="h-[52px] w-full text-[17px]" onClick={onReload}>
                {copy.reload}
              </Button>
            ) : (
              <Button
                data-testid={`${id}-confirm`}
                className="h-[52px] w-full text-[17px]"
                disabled={sending || !termsAccepted || !canSubmit(draft.answers)}
                onClick={onSend}
              >
                {sending ? copy.sending : copy.send}
              </Button>
            )}
            <Button variant="ghost" className="h-11 w-full text-[15px] text-verified-ink" disabled={sending} onClick={onClose}>
              {copy.close}
            </Button>
          </div>
        </div>
      )}
    </li>
  )
}
