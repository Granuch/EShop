'use server'

import { cookies } from "next/headers"
import { gatewayFetch } from "./api"
import { ACCESS_COOKIE, REFRESH_COOKIE, accessCookieOptions, refreshCookieOptions } from "./authCookies"
import { problemMessages, readProblem } from "./problem"

// The email flows behind the links Notification sends (<domain>/confirm-email, /reset-password) and the forms that
// start them. Server actions, so the gateway is reached through API_BASE_URL with the visitor's address.

export type FlowState = { ok: boolean; messages: string[] } | null

async function post(path: string, body: object, fallback: string): Promise<FlowState & {}> {
    let res: Response
    try {
        const init = { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) }
        res = await gatewayFetch(path, init)
    }
    catch {
        return { ok: false, messages: ["The shop could not be reached. Please try again"] }
    }

    const data = await readProblem(res)
    if (res.ok) {
        const message = (data as { message?: string } | null)?.message
        return { ok: true, messages: message ? [message] : [] }
    }
    return { ok: false, messages: problemMessages(res.status, data, res.headers.get("retry-after"), fallback) }
}

function field(formData: FormData, name: string): string {
    const value = formData.get(name)
    return typeof value === "string" ? value : ""
}

// After a confirmation the access token still says email_verified=false, and ordering reads that claim, so a
// signed-in user gets a fresh pair. A refused refresh only means they sign in again later; the confirmation stands.
async function refreshSignedInSession(): Promise<boolean> {
    const jar = await cookies()
    const refreshToken = jar.get(REFRESH_COOKIE)?.value
    if (!refreshToken) return false

    try {
        const res = await gatewayFetch("/api/v1/auth/refresh-token", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ refreshToken })
        })
        if (!res.ok) return false
        const data = await res.json()
        jar.set(ACCESS_COOKIE, data.accessToken, accessCookieOptions(data.expiresIn))
        jar.set(REFRESH_COOKIE, data.refreshToken, refreshCookieOptions)
        return true
    }
    catch {
        return false
    }
}

export async function confirmEmail(_: FlowState, formData: FormData): Promise<FlowState> {
    const result = await post("/api/v1/auth/confirm-email",
        { userId: field(formData, "userId"), token: field(formData, "token") },
        "The email address could not be confirmed")

    if (!result.ok) {
        // Auth.InvalidToken and Auth.UserNotFound both mean this link cannot work; the page offers a new one.
        return { ok: false, messages: ["This confirmation link is invalid or has expired. Request a new one below."] }
    }
    await refreshSignedInSession()
    return { ok: true, messages: ["Your email address is confirmed. You can now place orders."] }
}

export async function resendConfirmation(_: FlowState, formData: FormData): Promise<FlowState> {
    return post("/api/v1/auth/resend-confirmation", { email: field(formData, "email") },
        "The confirmation email could not be sent")
}

export async function forgotPassword(_: FlowState, formData: FormData): Promise<FlowState> {
    return post("/api/v1/auth/forgot-password", { email: field(formData, "email") },
        "The reset email could not be sent")
}

export async function resetPassword(_: FlowState, formData: FormData): Promise<FlowState> {
    const newPassword = field(formData, "newPassword")
    if (newPassword !== field(formData, "confirmPassword")) {
        return { ok: false, messages: ["The two passwords do not match"] }
    }

    const result = await post("/api/v1/auth/reset-password",
        { userId: field(formData, "userId"), token: field(formData, "token"), newPassword },
        "The password could not be reset")

    if (!result.ok && result.messages.some((m) => m === "Invalid token." || m === "Invalid password reset request")) {
        return { ok: false, messages: ["This reset link is invalid, expired or already used. Request a new one."] }
    }
    return result.ok
        ? { ok: true, messages: ["Your password has been changed. Sign in with your new password."] }
        : result
}
