import { useParams } from 'react-router'

export default function Verify() {
  const { nodeId } = useParams()
  return (
    <main className="p-6">
      <h1 className="text-2xl font-semibold">Verify</h1>
      <p className="text-sm text-neutral-600">Node {nodeId} — supplier form arrives in a later task.</p>
    </main>
  )
}
