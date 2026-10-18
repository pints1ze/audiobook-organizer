import { Link } from 'react-router-dom'

export default function NotFoundPage() {
  return (
    <>
      <h1 className="text-2xl font-semibold">Page not found</h1>
      <p className="mt-4">
        <Link to="/" className="rounded underline underline-offset-4">
          Back to the dashboard
        </Link>
      </p>
    </>
  )
}
