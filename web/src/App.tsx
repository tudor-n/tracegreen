import { lazy, Suspense } from 'react'
import { BrowserRouter, Route, Routes } from 'react-router'
import Supplier from './pages/Supplier.tsx'
import Verify from './pages/Verify.tsx'

// The dashboard is loaded on demand, so the supplier's phone pages do not download React Flow and the layout code.
const Dashboard = lazy(() => import('./pages/Dashboard.tsx'))

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route
          path="/"
          element={
            <Suspense fallback={null}>
              <Dashboard />
            </Suspense>
          }
        />
        <Route path="/verify/:nodeId" element={<Verify />} />
        <Route path="/supplier" element={<Supplier />} />
      </Routes>
    </BrowserRouter>
  )
}
