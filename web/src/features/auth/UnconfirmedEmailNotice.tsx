import { useState } from 'react'
import { ApiError } from '../../lib/apiClient'
import { resendVerificationEmail } from './emailApi'

/**
 * Nudge shown to a signed-in user whose address is still unconfirmed (#69).
 * Informational only — nothing in the MVP is gated on confirmation, so this
 * never blocks the page it sits on.
 */
export function UnconfirmedEmailNotice({ email }: { email: string }) {
  const [state, setState] = useState<'idle' | 'sending' | 'sent' | 'failed'>('idle')
  const [error, setError] = useState('')

  async function handleResend() {
    setState('sending')
    setError('')
    try {
      await resendVerificationEmail({ email })
      setState('sent')
    } catch (caught) {
      setState('failed')
      setError(
        caught instanceof ApiError && caught.status === 429
          ? 'too many requests — please wait a few minutes.'
          : 'that didn\u2019t work, please try again shortly.',
      )
    }
  }

  if (state === 'sent') {
    return (
      <p className="form-success" role="status">
        A new confirmation link is on its way to {email}.
      </p>
    )
  }

  return (
    <div className="form-errors" role="status">
      <p style={{ margin: 0 }}>
        Your email address isn't confirmed yet.{' '}
        <button
          type="button"
          className="link-button"
          onClick={handleResend}
          disabled={state === 'sending'}
        >
          {state === 'sending' ? 'Sending…' : 'Send a new confirmation link'}
        </button>
        {state === 'failed' && ` \u2014 ${error}`}
      </p>
    </div>
  )
}
