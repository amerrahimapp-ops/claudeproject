import '@testing-library/jest-dom/vitest'

// jsdom doesn't implement matchMedia, which AntD's grid/responsive-observer
// (used by Row/Form/Table layout) subscribes to on mount. Without this,
// any test rendering a Form or Table throws "matchMedia is not a function".
if (typeof window !== 'undefined' && !window.matchMedia) {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    }),
  })
}
