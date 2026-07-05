import { useAuth } from '../features/auth/authContext'

/** Placeholder until the binder grid ships (BACKLOG #20). */
export function BinderPage() {
  const { user } = useAuth()

  return (
    <section>
      <h1>Binder</h1>
      <p>Welcome back, {user?.displayName}. Your collection will appear here.</p>
    </section>
  )
}
