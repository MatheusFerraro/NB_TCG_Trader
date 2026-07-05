import { Link } from 'react-router-dom'
import { useAuth } from '../features/auth/authContext'

export function HomePage() {
  const { status } = useAuth()

  return (
    <section className="home">
      <h1>Your card collection, organized.</h1>
      <p>
        Import your spreadsheet, build a digital binder, and connect with local
        buyers in Moncton and Ipaussu.
      </p>
      {status === 'authenticated' ? (
        <Link className="cta" to="/binder">
          Open your binder
        </Link>
      ) : (
        <Link className="cta" to="/register">
          Get started
        </Link>
      )}
    </section>
  )
}
