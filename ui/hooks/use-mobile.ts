import * as React from "react"

const MOBILE_BREAKPOINT = 768
const MOBILE_QUERY = `(max-width: ${MOBILE_BREAKPOINT - 1}px)`

function subscribe(onChange: () => void) {
  const mql = window.matchMedia(MOBILE_QUERY)
  mql.addEventListener("change", onChange)
  return () => mql.removeEventListener("change", onChange)
}

// shadcn's version kept the answer in state and set it inside an effect, which the repo's ESLint config
// rejects (react-hooks/set-state-in-effect). Reading the media query through useSyncExternalStore gives the
// same result: false on the server and the first client render, then the live value.
export function useIsMobile() {
  return React.useSyncExternalStore(
    subscribe,
    () => window.matchMedia(MOBILE_QUERY).matches,
    () => false
  )
}
