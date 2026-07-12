import { theme, type ThemeConfig } from 'antd'

/**
 * App-wide Ant Design theme.
 *
 * Design intent (see design spec, "anti-AI-slop" philosophy):
 * - Dark mode by default.
 * - Dense, functional layout — not spacious/marketing-style.
 * - Single accent color, flat surfaces.
 * - No gradients, no heavy shadows, no animations.
 *
 * Light and dark share every token except the algorithm — this is
 * deliberately just an algorithm swap (theme.darkAlgorithm vs.
 * theme.defaultAlgorithm), not a redesign of the visual language. See
 * `resolveAppTheme` / `web/src/App.tsx` for how the user's saved `theme`
 * preference (GET /api/v1/me/preferences) picks between the two.
 */
const sharedTokens: ThemeConfig = {
  token: {
    colorPrimary: '#1677ff',
    borderRadius: 2,
    // Semantic colors left at AntD defaults (success/warning/error/info).
    motion: false,
  },
  components: {
    Layout: {
      headerPadding: '0 16px',
    },
    Button: {
      borderRadius: 2,
    },
    Card: {
      borderRadiusLG: 2,
    },
  },
}

export const darkTheme: ThemeConfig = {
  ...sharedTokens,
  algorithm: theme.darkAlgorithm,
}

export const lightTheme: ThemeConfig = {
  ...sharedTokens,
  algorithm: theme.defaultAlgorithm,
}

/** Back-compat alias — dark remains the app's default theme. */
export const appTheme: ThemeConfig = darkTheme
