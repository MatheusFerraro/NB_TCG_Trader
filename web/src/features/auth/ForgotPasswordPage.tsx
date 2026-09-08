import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { ApiError } from '../../lib/apiClient'
import { forgotPassword } from './emailApi'

export function ForgotPasswordPage() {
  const [email, setEmail] = useState('')
  const [sent, setSent] = useState(false)
  const [errors, setErrors] = useState<string[]>([])
  const [submitting, setSubmitting] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setErrors([])
    setSubmitting(true)
    try {
      await forgotPassword({ email })
      setSent(true)
    } catch (error) {
      // A 429 from the email rate limiter is the one failure worth naming: the
      // user needs to know to wait rather than keep retrying.
      if (error instanceof ApiError) {
        setErrors(
          error.status === 429
            ? ['Too many requests. Please wait a few minutes and try again.']
            : error.fieldErrors.length > 0
              ? error.fieldErrors
              : [error.message],
        )
      } else {
        setErrors(['Could not reach the server. Please try again.'])
      }
    } finally {
      setSubmitting(false)
    }
  }

  // Deliberately the same confirmation whether or not the address is registered:
  // the API does not reveal that, and neither should the UI.
  if (sent) {
    return (
      <section className="auth-card">
        <h1>Check your email</h1>
        <p className="form-success" role="status">
          If an account exists for <strong>{email}</strong>, we've sent a link to reset its
          password. The link expires shortly and can be used once.
        </p>
        <p>
          Didn't get it? Check your spam folder, or{' '}
          <button type="button" className="link-button" onClick={() => setSent(false)}>
            try a different address
          </button>
          .
        </p>
        <p>
          <Link to="/login">Back to sign in</Link>
        </p>
      </section>
    )
  }

  return (
    <section className="auth-card">
      <h1>Forgot your password?</h1>
      <p className="profile-intro">
        Enter the email address on your account and we'll send you a link to choose a new
        password.
      </p>
      <form onSubmit={handleSubmit} noValidate>
        <label>
          Email
          <input
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            autoComplete="email"
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
          {submitting ? 'Sending…' : 'Send reset link'}
        </button>
      </form>
      <p>
        Remembered it? <Link to="/login">Sign in</Link>
      </p>
    </section>
  )
}
