import { theme, type ThemeConfig } from 'antd'

/**
 * App-wide Ant Design theme.
 *
 * Design intent (see design spec, "anti-AI-slop" philosophy):
 * - Dark mode by default.
 * - Dense, functional layout — not spacious/marketing-style.
 * - Single accent color, flat surfaces.
 * - No gradients, no heavy shadows, no animations.
 */
export const appTheme: ThemeConfig = {
  algorithm: theme.darkAlgorithm,
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
