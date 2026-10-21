import { BrowserRouter, Route, Routes } from 'react-router-dom'
import AppLayout from './components/layout/AppLayout'
import BookPage from './pages/BookPage'
import DashboardPage from './pages/DashboardPage'
import IncomingPage from './pages/IncomingPage'
import JobsPage from './pages/JobsPage'
import NamesPage from './pages/NamesPage'
import NotFoundPage from './pages/NotFoundPage'
import PreviewPage from './pages/PreviewPage'
import SettingsPage from './pages/SettingsPage'

export function AppRoutes() {
  return (
    <Routes>
      <Route element={<AppLayout />}>
        <Route path="/" element={<DashboardPage />} />
        <Route path="/incoming" element={<IncomingPage />} />
        <Route path="/books/:groupId" element={<BookPage />} />
        <Route path="/people" element={<NamesPage key="people" kind="people" />} />
        <Route path="/genres" element={<NamesPage key="genres" kind="genres" />} />
        <Route path="/preview" element={<PreviewPage />} />
        <Route path="/jobs" element={<JobsPage />} />
        <Route path="/settings" element={<SettingsPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  )
}

export default function App() {
  return (
    <BrowserRouter>
      <AppRoutes />
    </BrowserRouter>
  )
}
