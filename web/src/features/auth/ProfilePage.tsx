import { useState } from 'react'
import type { FormEvent } from 'react'
import { ApiError } from '../../lib/apiClient'
import { useAuth } from './authContext'

/**
 * Profile settings: the public seller fields (display name, location, contact
 * handles) behind PUT /auth/me. Account email/password are credentials and
 * stay out of scope. The route sits inside ProtectedRoute, and the form seeds
 * from the context user — present whenever the route renders.
 */
export function ProfilePage() {
  const { user, updateProfile } = useAuth()
  const [displayName, setDisplayName] = useState(user?.displayName ?? '')
  const [city, setCity] = useState(user?.city ?? '')
  const [country, setCountry] = useState(user?.country ?? '')
  const [contactEmail, setContactEmail] = useState(user?.contactEmail ?? '')
  const [discordHandle, setDiscordHandle] = useState(user?.discordHandle ?? '')
  const [instagramHandle, setInstagramHandle] = useState(user?.instagramHandle ?? '')
  const [errors, setErrors] = useState<string[]>([])
  const [saved, setSaved] = useState(false)
  const [submitting, setSubmitting] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setErrors([])
    setSaved(false)
    setSubmitting(true)
    try {
      // PUT semantics: send every field; a blank input clears the value.
      await updateProfile({
        displayName: displayName.trim(),
        city: city.trim() || null,
        country: country.trim() || null,
        contactEmail: contactEmail.trim() || null,
        discordHandle: discordHandle.trim() || null,
        instagramHandle: instagramHandle.trim() || null,
      })
      setSaved(true)
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
      <h1>Profile</h1>
      <p className="profile-intro">
        Buyers see these details on your marketplace listings. Share at least one
        contact channel so they can actually reach you.
      </p>
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
        <label>
          Contact email <span className="optional">(optional)</span>
          <input
            type="email"
            value={contactEmail}
            onChange={(e) => setContactEmail(e.target.value)}
            autoComplete="email"
            placeholder="where buyers can email you"
          />
          <small>Shown publicly on your listings — it can differ from your sign-in email.</small>
        </label>
        <label>
          Discord handle <span className="optional">(optional)</span>
          <input
            value={discordHandle}
            onChange={(e) => setDiscordHandle(e.target.value)}
            placeholder="e.g. alice#1234"
          />
        </label>
        <label>
          Instagram handle <span className="optional">(optional)</span>
          <input
            value={instagramHandle}
            onChange={(e) => setInstagramHandle(e.target.value)}
            placeholder="e.g. @alicecards"
          />
        </label>
        {errors.length > 0 && (
          <ul className="form-errors" role="alert">
            {errors.map((error) => (
              <li key={error}>{error}</li>
            ))}
          </ul>
        )}
        {saved && (
          <p className="form-success" role="status">
            Profile saved. Your listings now show the updated details.
          </p>
        )}
        <button type="submit" disabled={submitting}>
          {submitting ? 'Saving…' : 'Save profile'}
        </button>
      </form>
    </section>
  )
}
