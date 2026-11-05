// Both tokens live in httpOnly cookies, so the browser never sees them and every API call goes through this server.
// `secure` is always on: browsers exempt only localhost, so any other host must be served over HTTPS.
export const ACCESS_COOKIE = "access_token"
export const REFRESH_COOKIE = "refresh_token"

const base = { httpOnly: true, secure: true, sameSite: "lax" as const, path: "/" }

export function accessCookieOptions(expiresIn: number) {
    return { ...base, maxAge: expiresIn }
}

export const refreshCookieOptions = { ...base, maxAge: 60 * 60 * 24 * 30 }
