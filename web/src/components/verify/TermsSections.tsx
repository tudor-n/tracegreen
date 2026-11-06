import type { TermsSection } from '@/api/client'

// The numbered sections of the supplier terms, as plain text from the API, on the printable page /terms/supplier.

export function TermsSections({ sections }: { sections: TermsSection[] }) {
  return (
    <ol className="flex flex-col gap-4">
      {sections.map((section) => (
        <li key={section.number} className="flex flex-col gap-1.5">
          <h3 className="text-base font-semibold">
            {section.number}. {section.heading}
          </h3>
          {section.paragraphs.map((paragraph, i) => (
            // A published version's paragraphs never move, so their position is a stable key.
            <p key={`${section.number}.${i}`} className="text-base leading-relaxed">
              {paragraph}
            </p>
          ))}
        </li>
      ))}
    </ol>
  )
}
