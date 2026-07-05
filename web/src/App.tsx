import { BrowserRouter, Route, Routes } from 'react-router-dom'
import { AppShell } from './AppShell'
import { AuthProvider } from './features/auth/AuthProvider'
import { LoginPage } from './features/auth/LoginPage'
import { RegisterPage } from './features/auth/RegisterPage'
import { AddCardPage } from './pages/AddCardPage'
import { BinderPage } from './pages/BinderPage'
import { HomePage } from './pages/HomePage'
import { ImportPage } from './pages/ImportPage'
import { ImportReviewPage } from './pages/ImportReviewPage'
import { NotFoundPage } from './pages/NotFoundPage'
import { ProtectedRoute } from './routes/ProtectedRoute'
import './App.css'

function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route element={<AppShell />}>
            <Route index element={<HomePage />} />
            <Route path="login" element={<LoginPage />} />
            <Route path="register" element={<RegisterPage />} />
            <Route element={<ProtectedRoute />}>
              <Route path="binder" element={<BinderPage />} />
              <Route path="binder/add" element={<AddCardPage />} />
              <Route path="binder/import" element={<ImportPage />} />
              <Route path="binder/import/:jobId" element={<ImportReviewPage />} />
            </Route>
            <Route path="*" element={<NotFoundPage />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  )
}

export default App
