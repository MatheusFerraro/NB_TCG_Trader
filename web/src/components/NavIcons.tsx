/**
 * Inline navigation glyphs. Hand-rolled instead of pulling an icon package:
 * nine 24px strokes do not justify a dependency, and inlining keeps them on the
 * same paint as the sidebar (no icon-font flash, no extra request).
 *
 * Every glyph is decorative — the adjacent text label carries the meaning — so
 * they are aria-hidden and focusable="false" (IE/Edge legacy SVG focus quirk).
 */

import type { ReactNode } from 'react'

type IconProps = { className?: string }

function Svg({ children, className }: IconProps & { children: ReactNode }) {
  return (
    <svg
      className={className ? `nav-icon ${className}` : 'nav-icon'}
      viewBox="0 0 24 24"
      width="18"
      height="18"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      {children}
    </svg>
  )
}

export function HomeIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M3.5 10.5 12 4l8.5 6.5V19a1.5 1.5 0 0 1-1.5 1.5h-3.5V14h-7v6.5H5A1.5 1.5 0 0 1 3.5 19z" />
    </Svg>
  )
}

export function MarketIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M4 9h16l-1 10.5a1.5 1.5 0 0 1-1.5 1.5h-11A1.5 1.5 0 0 1 5 19.5z" />
      <path d="M8.5 9V6.5a3.5 3.5 0 0 1 7 0V9" />
    </Svg>
  )
}

export function BinderIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <rect x="4" y="3.5" width="16" height="17" rx="2" />
      <path d="M8 3.5v17M11 8h6M11 12h6" />
    </Svg>
  )
}

export function ImportIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M12 15.5V4m0 0L8.5 7.5M12 4l3.5 3.5" />
      <path d="M4.5 14.5v3.5a2 2 0 0 0 2 2h11a2 2 0 0 0 2-2v-3.5" />
    </Svg>
  )
}

export function ProfileIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <circle cx="12" cy="8.5" r="3.75" />
      <path d="M4.75 20a7.25 7.25 0 0 1 14.5 0" />
    </Svg>
  )
}

export function AdminIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M12 3.25 19 6v6c0 4.2-2.9 7.4-7 8.75C7.9 19.4 5 16.2 5 12V6z" />
      <path d="m9.25 12 2 2 3.5-3.75" />
    </Svg>
  )
}

export function SignInIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M10 4.5H6.5a2 2 0 0 0-2 2v11a2 2 0 0 0 2 2H10" />
      <path d="M15 8.5 18.5 12 15 15.5M18 12H9.5" />
    </Svg>
  )
}

export function RegisterIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <circle cx="10" cy="8.5" r="3.5" />
      <path d="M3.75 19.5a6.25 6.25 0 0 1 12.5 0" />
      <path d="M18.5 6.5v5M21 9h-5" />
    </Svg>
  )
}

export function SignOutIcon(props: IconProps) {
  return (
    <Svg {...props}>
      <path d="M14 4.5h3.5a2 2 0 0 1 2 2v11a2 2 0 0 1-2 2H14" />
      <path d="M8.5 8.5 5 12l3.5 3.5M5.5 12H14" />
    </Svg>
  )
}

/**
 * Brand mark: two offset rounded "cards" at 63:88 proportions with a highlight
 * on the front face. Drawn rather than shipped as a raster so it stays sharp at
 * any density and inherits the sidebar's own colours.
 */
export function BrandMark({ className }: IconProps) {
  return (
    <svg
      className={className ? `brand-mark ${className}` : 'brand-mark'}
      viewBox="0 0 32 32"
      width="28"
      height="28"
      aria-hidden="true"
      focusable="false"
    >
      <defs>
        <linearGradient id="brand-mark-face" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0%" stopColor="#c78bff" />
          <stop offset="100%" stopColor="#7c3aed" />
        </linearGradient>
      </defs>
      <rect
        x="4.5"
        y="6"
        width="14"
        height="19.5"
        rx="2.6"
        fill="currentColor"
        opacity="0.34"
        transform="rotate(-12 11.5 15.75)"
      />
      <rect x="12" y="5" width="15.5" height="21.6" rx="2.8" fill="url(#brand-mark-face)" />
      <path d="M15.6 20.4 19.9 11l4.2 9.4" stroke="#fff" strokeWidth="1.9" strokeLinecap="round" fill="none" />
    </svg>
  )
}
