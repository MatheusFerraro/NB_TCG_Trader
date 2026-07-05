import { Link } from 'react-router-dom'

export function NotFoundPage() {
  return (
    <section>
      <h1>Page not found</h1>
      <p>
        Nothing here. <Link to="/">Back to home</Link>
      </p>
    </section>
  )
}
