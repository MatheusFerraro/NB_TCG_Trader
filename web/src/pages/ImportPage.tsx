import { useId, useState } from 'react'
import type { FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { ApiError } from '../lib/apiClient'
import {
  hasImportExtension,
  importTemplateUrl,
  uploadImportJob,
} from '../features/import/importApi'

/**
 * CSV/XLSX import entry point (BACKLOG #21): download the template, upload a
 * file, then continue to the review screen — it handles both a fully matched
 * job (nothing to do) and one that needs reconciliation.
 */
export function ImportPage() {
  const navigate = useNavigate()
  const uid = useId()
  const [file, setFile] = useState<File | null>(null)
  const [errors, setErrors] = useState<string[]>([])
  const [uploading, setUploading] = useState(false)

  async function handleUpload(event: FormEvent) {
    event.preventDefault()
    if (!file) {
      setErrors(['Choose a .csv or .xlsx file to upload.'])
      return
    }
    if (!hasImportExtension(file.name)) {
      setErrors(['Only .csv and .xlsx files are accepted.'])
      return
    }

    setErrors([])
    setUploading(true)
    try {
      const job = await uploadImportJob(file)
      navigate(`/binder/import/${job.id}`)
    } catch (error) {
      if (error instanceof ApiError) {
        setErrors(error.fieldErrors.length > 0 ? error.fieldErrors : [error.message])
      } else {
        setErrors(['Could not reach the server. Please try again.'])
      }
      setUploading(false)
    }
  }

  return (
    <section>
      <header className="binder-header">
        <h1>Import cards</h1>
        <Link to="/binder">Back to binder</Link>
      </header>

      <div className="import-steps">
        <div className="import-step">
          <h2>1. Fill in the template</h2>
          <p>
            Start from our spreadsheet template — one card per row. Only the card
            name is required; set, number, quantity, condition, price, and
            for-sale are optional.
          </p>
          <a className="cta cta-outline" href={importTemplateUrl} download>
            Download the CSV template
          </a>
        </div>

        <form className="import-step" onSubmit={handleUpload}>
          <h2>2. Upload your file</h2>
          <p>
            We accept .csv and .xlsx files. We match every row against the card
            catalog; anything we cannot match automatically, you review next.
          </p>
          <div className="field">
            <label htmlFor={`${uid}-file`} className="field-label">
              Spreadsheet file
            </label>
            <input
              id={`${uid}-file`}
              type="file"
              accept=".csv,.xlsx"
              onChange={(e) => {
                setFile(e.target.files?.[0] ?? null)
                setErrors([])
              }}
            />
          </div>
          {errors.length > 0 && (
            <ul className="form-errors" role="alert">
              {errors.map((error) => (
                <li key={error}>{error}</li>
              ))}
            </ul>
          )}
          <div className="binder-card-actions">
            <button type="submit" disabled={uploading}>
              {uploading ? 'Uploading and matching…' : 'Upload and match'}
            </button>
          </div>
          {uploading && (
            <p className="catalog-hint" role="status">
              Matching your rows against the catalog — this can take a moment for
              bigger files.
            </p>
          )}
        </form>
      </div>
    </section>
  )
}
