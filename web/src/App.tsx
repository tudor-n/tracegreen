import { BrowserRouter, Route, Routes } from 'react-router'
import Dashboard from './pages/Dashboard.tsx'
import Verify from './pages/Verify.tsx'

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<Dashboard />} />
        <Route path="/verify/:nodeId" element={<Verify />} />
      </Routes>
    </BrowserRouter>
  )
}
