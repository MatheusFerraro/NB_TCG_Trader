import { useEffect, useId, useRef, useState } from 'react'
import type { ReactNode } from 'react'

interface FieldHelpProps {
  /** Accessible name for the trigger, e.g. "What is a set?" */
  label: string
  children: ReactNode
}

/**
 * Small "?" toggle that reveals a short explanation next to a form field.
 * Click-to-toggle (not hover) so it works with touch and keyboard; closes on
 * Escape or an outside click. Rendered outside <label> on purpose — a button
 * inside a label would also activate the labelled control.
 */
export function FieldHelp({ label, children }: FieldHelpProps) {
  const [open, setOpen] = useState(false)
  const popId = useId()
  const rootRef = useRef<HTMLSpanElement>(null)

  useEffect(() => {
    if (!open) return
    function onPointerDown(event: PointerEvent) {
      if (rootRef.current && !rootRef.current.contains(event.target as Node)) {
        setOpen(false)
      }
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setOpen(false)
      }
    }
    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open])

  return (
    <span className="field-help" ref={rootRef}>
      <button
        type="button"
        className="field-help-trigger"
        aria-label={label}
        aria-expanded={open}
        aria-controls={popId}
        onClick={() => setOpen((wasOpen) => !wasOpen)}
      >
        ?
      </button>
      {open && (
        <span role="note" id={popId} className="field-help-pop">
          {children}
        </span>
      )}
    </span>
  )
}
