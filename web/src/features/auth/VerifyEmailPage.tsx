import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { ApiError } from '../../lib/apiClient'
import { useAuth } from './authContext'
import { resendVerificationEmail, verifyEmail } from './emailApi'

type Status = 'verifying' | 'confirmed' | 'failed' | 'missing'

export function VerifyEmailPage() {
  const [searchParams] = useSearchParams()
  const userId = searchParams.get('userId') ?? ''
  const token = searchParams.get('token') ?? ''
  const { user, refreshUser } = useAuth()

  const [status, setStatus] = useState<Status>(userId && token ? 'verifying' : 'missing')
  const [message, setMessage] = useState('')

  // StrictMode runs effects twice in development. The token is single-use, so the
  // second run would hit an already-spent token; guard so it fires exactly once.
  const started = useRef(false)

  useEffect(() => {
    if (started.current || !userId || !token) {
      return
    }
    started.current = true

    void (async () => {
      try {
        await verifyEmail({ userId, token })
        setStatus('confirmed')
        // Pull a fresh /auth/me so the signed-in session stops showing the
        // "unconfirmed" state without a manual reload.
        await refreshUser()
      } catch (error) {
        setStatus('failed')
        setMessage(
          error instanceof ApiError
            ? error.message
            : 'Could not reach the server. Please try again.',
        )
      }
    })()
  }, [userId, token, refreshUser])

  if (status === 'verifying') {
    return (
      <section className="auth-card">
        <h1>Confirming your email…</h1>
        <p role="status">One moment.</p>
      </section>
    )
  }

  if (status === 'confirmed') {
    return (
      <section className="auth-card">
        <h1>Email confirmed</h1>
        <p className="form-success" role="status">
          Thanks — your email address is confirmed.
        </p>
        <p>
          <Link to={user ? '/binder' : '/login'}>{user ? 'Go to my binder' : 'Sign in'}</Link>
        </p>
      </section>
    )
  }

  return (
    <section className="auth-card">
      <h1>{status === 'missing' ? 'Confirmation link incomplete' : 'Confirmation failed'}</h1>
      <p className="form-errors" role="alert">
        {status === 'missing'
          ? 'This link is missing information. Open the most recent confirmation email again.'
          : message}
      </p>
      <ResendForm defaultEmail={user?.email ?? ''} />
      <p>
        <Link to="/login">Back to sign in</Link>
      </p>
    </section>
  )
}

/** Requests a fresh confirmation email. Confirms identically for any address. */
function ResendForm({ defaultEmail }: { defaultEmail: string }) {
  const [email, setEmail] = useState(defaultEmail)
  const [sent, setSent] = useState(false)
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  if (sent) {
    return (
      <p className="form-success" role="status">
        If that address needs confirming, a new link is on its way.
      </p>
    )
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError('')
    setSubmitting(true)
    try {
      await resendVerificationEmail({ email })
      setSent(true)
    } catch (caught) {
      setError(
        caught instanceof ApiError && caught.status === 429
          ? 'Too many requests. Please wait a few minutes and try again.'
          : 'Could not send a new link. Please try again.',
      )
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <form onSubmit={handleSubmit} noValidate>
      <label>
        Send a new confirmation link to
        <input
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
          autoComplete="email"
          required
        />
      </label>
      {error && (
        <ul className="form-errors" role="alert">
          <li>{error}</li>
        </ul>
      )}
      <button type="submit" disabled={submitting}>
        {submitting ? 'Sending…' : 'Send new link'}
      </button>
    </form>
  )
}
