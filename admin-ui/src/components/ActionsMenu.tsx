import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'

export type ActionItem = {
  label: string
  onClick: () => void
  variant?: 'default' | 'danger'
  disabled?: boolean
}

// Menu deroulant generique : ferme au clic exterieur, a Escape, au scroll/resize,
// et s'ouvre vers le haut s'il n'y a pas la place en dessous. Rendu via un portail
// (position fixed calculee depuis getBoundingClientRect) pour ne jamais etre
// coupe par le overflow:auto d'un tableau parent.
export default function ActionsMenu({ items }: { items: ActionItem[] }) {
  const [open, setOpen] = useState(false)
  const [coords, setCoords] = useState<{ top: number; left: number; openUp: boolean }>({
    top: 0,
    left: 0,
    openUp: false,
  })
  const triggerRef = useRef<HTMLButtonElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)

  const place = () => {
    const rect = triggerRef.current?.getBoundingClientRect()
    if (!rect) return
    const menuHeight = items.length * 36 + 12
    const spaceBelow = window.innerHeight - rect.bottom
    const openUp = spaceBelow < menuHeight && rect.top > menuHeight
    setCoords({
      top: openUp ? rect.top - menuHeight - 6 : rect.bottom + 6,
      left: Math.min(rect.right - 180, window.innerWidth - 192),
      openUp,
    })
  }

  useEffect(() => {
    if (!open) return
    place()

    const onDocClick = (e: MouseEvent) => {
      if (menuRef.current?.contains(e.target as Node) || triggerRef.current?.contains(e.target as Node)) return
      setOpen(false)
    }
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false)
    const onScrollOrResize = () => setOpen(false)

    document.addEventListener('mousedown', onDocClick)
    document.addEventListener('keydown', onKey)
    window.addEventListener('scroll', onScrollOrResize, true)
    window.addEventListener('resize', onScrollOrResize)
    return () => {
      document.removeEventListener('mousedown', onDocClick)
      document.removeEventListener('keydown', onKey)
      window.removeEventListener('scroll', onScrollOrResize, true)
      window.removeEventListener('resize', onScrollOrResize)
    }
  }, [open])

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        className="actions-trigger"
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((v) => !v)}
      >
        <svg width="16" height="16" viewBox="0 0 16 16" fill="none">
          <circle cx="8" cy="3" r="1.4" fill="currentColor" />
          <circle cx="8" cy="8" r="1.4" fill="currentColor" />
          <circle cx="8" cy="13" r="1.4" fill="currentColor" />
        </svg>
      </button>
      {open &&
        createPortal(
          <div
            ref={menuRef}
            role="menu"
            className={`actions-menu${coords.openUp ? ' up' : ''}`}
            style={{ top: coords.top, left: coords.left }}
          >
            {items.map((item, i) => (
              <button
                key={i}
                role="menuitem"
                className={item.variant === 'danger' ? 'danger' : ''}
                disabled={item.disabled}
                onClick={() => {
                  setOpen(false)
                  item.onClick()
                }}
              >
                {item.label}
              </button>
            ))}
          </div>,
          document.body,
        )}
    </>
  )
}
