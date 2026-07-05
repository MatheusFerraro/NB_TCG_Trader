import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, Navigate, useNavigate } from 'react-router-dom'
import { ApiError } from '../../lib/apiClient'
import { useAuth } from './authContext'

export function RegisterPage() {
  const { status, register } = useAuth()
  const navigate = useNavigate()
  const [displayName, setDisplayName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [city, setCity] = useState('')
  const [country, setCountry] = useState('')
  const [errors, setErrors] = useState<string[]>([])
  const [submitting, setSubmitting] = useState(false)

  if (status === 'authenticated') {
    return <Navigate to="/binder" replace />
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setErrors([])
    setSubmitting(true)
    try {
      await register({
        displayName,
        email,
        password,
        city: city.trim() || undefined,
        country: country.trim() || undefined,
      })
      navigate('/binder', { replace: true })
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
      <h1>Create account</h1>
      <form onSubmit={handleSubmit} noValidate>
        <label>
          Display name
          <input
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            autoComplete="nickname"
            required
          />
        </label>
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
        <label>
          Password
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="new-password"
            required
            minLength={12}
          />
          <small>At least 12 characters, with an uppercase letter, a lowercase letter, a digit, and a symbol.</small>
        </label>
        <label>
          City <span className="optional">(optional)</span>
          <input
            value={city}
            onChange={(e) => setCity(e.target.value)}
            autoComplete="address-level2"
            placeholder="Moncton, Ipaussu…"
          />
        </label>
        <label>
          Country <span className="optional">(optional)</span>
          <input
            value={country}
            onChange={(e) => setCountry(e.target.value)}
            autoComplete="country-name"
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
          {submitting ? 'Creating…' : 'Create account'}
        </button>
      </form>
      <p>
        Already registered? <Link to="/login">Sign in</Link>
      </p>
    </section>
  )
}
