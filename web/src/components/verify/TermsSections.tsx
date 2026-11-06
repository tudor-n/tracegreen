import type { TermsSection } from '@/api/client'

// The numbered sections of the supplier terms, as plain text from the API, on the printable page /terms/supplier.

export function TermsSections({ sections }: { sections: TermsSection[] }) {
  return (
    <ol className="flex flex-col gap-6">
      {sections.map((section) => (
        <li key={section.number} className="flex flex-col gap-2">
          <h3 className="text-[17px] font-semibold tracking-[-0.015em]">
            {section.number}. {section.heading}
          </h3>
          {section.paragraphs.map((paragraph, i) => (
            // A published version's paragraphs never move, so their position is a stable key.
            <p key={`${section.number}.${i}`} className="text-[15px] leading-relaxed text-ink-2">
              {paragraph}
            </p>
          ))}
        </li>
      ))}
    </ol>
  )
}
