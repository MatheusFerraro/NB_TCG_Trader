import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { ApiError } from '../../lib/apiClient'
import { resetPassword } from './emailApi'

export function ResetPasswordPage() {
  const [searchParams] = useSearchParams()
  const email = searchParams.get('email') ?? ''
  const token = searchParams.get('token') ?? ''

  const [password, setPassword] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const [done, setDone] = useState(false)
  const [errors, setErrors] = useState<string[]>([])
  const [submitting, setSubmitting] = useState(false)

  // The link carries both values; without them there is nothing to submit.
  if (!email || !token) {
    return (
      <section className="auth-card">
        <h1>Reset link incomplete</h1>
        <p className="form-errors" role="alert">
          This link is missing information. Open the most recent reset email again, or request
          a new link.
        </p>
        <p>
          <Link to="/forgot-password">Request a new link</Link>
        </p>
      </section>
    )
  }

  if (done) {
    return (
      <section className="auth-card">
        <h1>Password updated</h1>
        <p className="form-success" role="status">
          Your password has been changed and every signed-in session was signed out. Sign in
          with your new password.
        </p>
        <p>
          <Link to="/login">Go to sign in</Link>
        </p>
      </section>
    )
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()

    // Checked client-side only: the API takes one password, so a mismatch is a
    // typo to catch here rather than a round-trip.
    if (password !== confirmation) {
      setErrors(['The two passwords do not match.'])
      return
    }

    setErrors([])
    setSubmitting(true)
    try {
      await resetPassword({ email, token, newPassword: password })
      setDone(true)
    } catch (error) {
      if (error instanceof ApiError) {
        setErrors(error.fieldErrors.length > 0 ? error.fieldErrors : [error.message])
      } else {
        setErrors(['Could not reach the server. Please try again.'])
      }
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <section className="auth-card">
      <h1>Choose a new password</h1>
      <p className="profile-intro">
        Setting a new password for <strong>{email}</strong>.
      </p>
      <form onSubmit={handleSubmit} noValidate>
        <label>
          New password
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="new-password"
            minLength={12}
            required
          />
          <small>At least 12 characters, with upper and lower case, a digit, and a symbol.</small>
        </label>
        <label>
          Confirm new password
          <input
            type="password"
            value={confirmation}
            onChange={(e) => setConfirmation(e.target.value)}
            autoComplete="new-password"
            minLength={12}
            required
          />
        </label>
        {errors.length > 0 && (
          <ul className="form-errors" role="alert">
            {errors.map((error) => (
              <li key={error}>{error}</li>
            ))}
          </ul>
        )}
        <button type="submit" disabled={submitting}>
          {submitting ? 'Saving…' : 'Set new password'}
        </button>
      </form>
      <p>
        Link expired? <Link to="/forgot-password">Request a new one</Link>
      </p>
    </section>
  )
}
