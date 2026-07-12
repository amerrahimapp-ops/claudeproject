import { ConfigProvider } from 'antd'
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query'
import { BrowserRouter } from 'react-router-dom'
import { AuthProvider } from './context/AuthProvider'
import { useAuth } from './context/useAuth'
import { AppRoutes } from './routes/AppRoutes'
import { darkTheme, lightTheme } from './theme/theme'
import { fetchMyPreferences } from './api/preferences'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false,
      retry: 1,
    },
  },
})

/**
 * Picks the AntD theme (light/dark algorithm) from the user's saved
 * preference. Shares the `['myPreferences']` query cache with
 * AuthenticatedLayout.tsx (same queryKey, same queryFn) — react-query
 * dedupes the two subscriptions rather than double-fetching. Must live
 * inside QueryClientProvider/AuthProvider (needs both the query cache and
 * `isAuthenticated`), which is why ConfigProvider was moved to wrap
 * BrowserRouter/AppRoutes here instead of wrapping QueryClientProvider as
 * it did previously.
 *
 * Before login / before the preference has loaded, this defaults to dark
 * (today's existing behavior) rather than flashing light.
 */
function ThemedApp() {
  const { isAuthenticated } = useAuth()

  const { data: preferences } = useQuery({
    queryKey: ['myPreferences'],
    queryFn: fetchMyPreferences,
    enabled: isAuthenticated,
  })

  const themeConfig = preferences?.theme === 'Light' ? lightTheme : darkTheme

  return (
    <ConfigProvider theme={themeConfig}>
      <BrowserRouter>
        <AppRoutes />
      </BrowserRouter>
    </ConfigProvider>
  )
}

function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <ThemedApp />
      </AuthProvider>
    </QueryClientProvider>
  )
}

export default App
