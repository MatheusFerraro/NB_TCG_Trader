import { BrowserRouter, Route, Routes } from 'react-router-dom'
import { AppShell } from './AppShell'
import { AdminLayout } from './features/admin/AdminLayout'
import { AuthProvider } from './features/auth/AuthProvider'
import { ForgotPasswordPage } from './features/auth/ForgotPasswordPage'
import { LoginPage } from './features/auth/LoginPage'
import { ProfilePage } from './features/auth/ProfilePage'
import { RegisterPage } from './features/auth/RegisterPage'
import { ResetPasswordPage } from './features/auth/ResetPasswordPage'
import { VerifyEmailPage } from './features/auth/VerifyEmailPage'
import { AddCardPage } from './pages/AddCardPage'
import { AdminActivityPage } from './pages/AdminActivityPage'
import { AdminAuditLogPage } from './pages/AdminAuditLogPage'
import { AdminDashboardPage } from './pages/AdminDashboardPage'
import { AdminUserDetailPage } from './pages/AdminUserDetailPage'
import { AdminUsersPage } from './pages/AdminUsersPage'
import { BinderPage } from './pages/BinderPage'
import { HomePage } from './pages/HomePage'
import { ImportPage } from './pages/ImportPage'
import { ImportReviewPage } from './pages/ImportReviewPage'
import { ListingDetailPage } from './pages/ListingDetailPage'
import { MarketplacePage } from './pages/MarketplacePage'
import { NotFoundPage } from './pages/NotFoundPage'
import { AdminRoute } from './routes/AdminRoute'
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
            {/* Reached from an emailed link, so necessarily public: the people who
                need them are the ones who cannot sign in (#69). */}
            <Route path="verify-email" element={<VerifyEmailPage />} />
            <Route path="forgot-password" element={<ForgotPasswordPage />} />
            <Route path="reset-password" element={<ResetPasswordPage />} />
            {/* Public: anonymous buyers can browse and open listings. */}
            <Route path="marketplace" element={<MarketplacePage />} />
            <Route path="marketplace/:itemId" element={<ListingDetailPage />} />
            <Route element={<ProtectedRoute />}>
              <Route path="binder" element={<BinderPage />} />
              <Route path="binder/add" element={<AddCardPage />} />
              <Route path="binder/import" element={<ImportPage />} />
              <Route path="binder/import/:jobId" element={<ImportReviewPage />} />
              <Route path="profile" element={<ProfilePage />} />
            </Route>
            {/* Admin hub: role-gated in the client, Admin-policy-enforced by the API. */}
            <Route element={<AdminRoute />}>
              <Route path="admin" element={<AdminLayout />}>
                <Route index element={<AdminDashboardPage />} />
                <Route path="users" element={<AdminUsersPage />} />
                <Route path="users/:userId" element={<AdminUserDetailPage />} />
                <Route path="activity" element={<AdminActivityPage />} />
                <Route path="audit" element={<AdminAuditLogPage />} />
              </Route>
            </Route>
            <Route path="*" element={<NotFoundPage />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  )
}

export default App
