"""Whether an extracted value can be found in the PDF's own text (F9): a hint for the person who
checks the fields before the import, never a check that passes or fails the import. Text is
compared without case and whitespace runs; numbers in both decimal conventions and with or
without thousands separators; dates as ISO, day.month.year and day/month/year."""

from __future__ import annotations

import re
from decimal import Decimal, InvalidOperation


def normalise(text: str) -> str:
    return " ".join(text.lower().split())


def found_text(value: str, document: str) -> bool:
    v = normalise(value)
    return bool(v) and v in document


def parse_amount(value: str) -> Decimal | None:
    """An amount as printed, read by the API's own rule (PdfImportService.Amount): with both
    marks the last is the decimal mark; one kind groups thousands when it repeats or has exactly
    three digits after it ("1.200", "9,840"), and is the decimal mark otherwise ("12,50")."""
    t = value.strip().replace(" ", "").replace("\u00a0", "")
    if not t or len(t) > 32:
        return None
    dot, comma = t.rfind("."), t.rfind(",")
    if dot >= 0 and comma >= 0:
        t = t.replace(".", "").replace(",", ".") if comma > dot else t.replace(",", "")
    elif dot >= 0 or comma >= 0:
        mark = "." if dot >= 0 else ","
        at = max(dot, comma)
        thousands = t.count(mark) > 1 or len(t) - at - 1 == 3
        t = t.replace(mark, "") if thousands else t.replace(mark, ".")
    try:
        return Decimal(t)
    except InvalidOperation:
        return None


def number_forms(value: str) -> list[str]:
    """The ways an amount may be printed: 1234.5 → 1234.50, 1234,50, 1,234.50, 1.234,50,
    1 234,50; a whole number also without decimals (1234, 1,234, 1.234, 1 234)."""
    number = parse_amount(value)
    if number is None:
        return [value.strip()]
    whole, _, frac = f"{abs(number):.2f}".partition(".")
    sign = "-" if number < 0 else ""
    groups = f"{int(whole):,}".split(",")
    forms = set()
    for sep, dec in ((",", "."), (".", ","), (" ", ","), ("", "."), ("", ",")):
        head = sign + sep.join(groups)
        forms.add(f"{head}{dec}{frac}")
        if number == number.to_integral_value():
            forms.add(head)
    return sorted(forms, key=len, reverse=True)


def found_number(value: str, document: str) -> bool:
    if not value.strip():
        return False
    compact = document.replace(" ", " ")
    for form in number_forms(value):
        if re.search(rf"(?<![\d.,]){re.escape(form.lower())}(?![\d])", compact):
            return True
    return False


def found_date(value: str, document: str) -> bool:
    match = re.fullmatch(r"(\d{4})-(\d{2})-(\d{2})", value.strip())
    if match is None:
        return False
    y, m, d = match.groups()
    forms = [
        f"{y}-{m}-{d}",
        f"{d}.{m}.{y}",
        f"{d}/{m}/{y}",
        f"{int(d)}.{int(m)}.{y}",
        f"{d}-{m}-{y}",
    ]
    return any(f in document for f in forms)
