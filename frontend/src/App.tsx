import { NavLink, Route, Routes } from 'react-router-dom'
import './App.css'
import { ThemeToggle } from './components/ThemeToggle/ThemeToggle.tsx'
import { OffersPage } from './pages/Offers/OffersPage.tsx'
import { ScansPage } from './pages/Scans/ScansPage.tsx'
import { CvPage } from './pages/Cv/CvPage.tsx'
import { SourcesPage } from './pages/Sources/SourcesPage.tsx'
import { SettingsPage } from './pages/Settings/SettingsPage.tsx'

const NAV = [
  { to: '/', label: 'Offers', end: true },
  { to: '/scans', label: 'Scans' },
  { to: '/cv', label: 'CV & Profile' },
  { to: '/sources', label: 'Sources' },
  { to: '/settings', label: 'Settings' },
]

export function App() {
  return (
    <div className="app-shell">
      <header className="app-header">
        <div className="container app-header__inner">
          <div className="app-brand">
            <span className="app-brand__mark" aria-hidden="true" />
            <span className="app-brand__name">Job Offer Matcher</span>
          </div>
          <div className="app-header__right">
            <nav className="app-nav" aria-label="Primary">
              {NAV.map((item) => (
                <NavLink
                  key={item.to}
                  to={item.to}
                  end={item.end}
                  className={({ isActive }) =>
                    isActive ? 'app-nav__link app-nav__link--active' : 'app-nav__link'
                  }
                >
                  {item.label}
                </NavLink>
              ))}
            </nav>
            <ThemeToggle />
          </div>
        </div>
      </header>

      <main className="container app-main">
        <Routes>
          <Route path="/" element={<OffersPage />} />
          <Route path="/scans" element={<ScansPage />} />
          <Route path="/cv" element={<CvPage />} />
          <Route path="/sources" element={<SourcesPage />} />
          <Route path="/settings" element={<SettingsPage />} />
        </Routes>
      </main>
    </div>
  )
}
