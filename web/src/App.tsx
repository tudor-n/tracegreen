import { lazy, type ReactNode, Suspense } from 'react'
import { BrowserRouter, Route, Routes } from 'react-router'
import Verify from './pages/Verify.tsx'

// The buyer's app is loaded on demand, so the supplier's phone page does not download it. Its three screens share
// one shell (sidebar and sync): Overview at /, Suppliers at /suppliers, the audit log at /audit.
const AppShell = lazy(() => import('./pages/AppShell.tsx'))
const Overview = lazy(() => import('./pages/Overview.tsx'))
const Suppliers = lazy(() => import('./pages/Suppliers.tsx'))
const AuditLog = lazy(() => import('./pages/AuditLog.tsx'))

const lazily = (element: ReactNode) => <Suspense fallback={null}>{element}</Suspense>

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={lazily(<AppShell />)}>
          <Route index element={lazily(<Overview />)} />
          <Route path="suppliers" element={lazily(<Suppliers />)} />
          <Route path="audit" element={lazily(<AuditLog />)} />
        </Route>
        <Route path="/verify/:nodeId" element={<Verify />} />
      </Routes>
    </BrowserRouter>
  )
}
