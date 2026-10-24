import { lazy, Suspense } from 'react'
import { BrowserRouter, Route, Routes } from 'react-router'
import Verify from './pages/Verify.tsx'

// The dashboard is loaded on demand, so the supplier's phone page does not download React Flow and the layout code.
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
      </Routes>
    </BrowserRouter>
  )
}
