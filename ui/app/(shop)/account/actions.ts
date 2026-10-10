'use server'

import { apiFetch } from "@/lib/api";

export type AccountResult = { success: true } | { success: false, message: string }

// Runs on the server, so the access token stays in its httpOnly cookie and the call carries the visitor's address.
export async function updateProfile(firstName: string, lastName: string): Promise<AccountResult> {
    return send("/api/v1/Account/profile", "PUT", { firstName, lastName })
}

export async function changePassword(currentPassword: string, newPassword: string): Promise<AccountResult> {
    return send("/api/v1/Account/change-password", "POST", { currentPassword, newPassword })
}

async function send(path: string, method: string, body: object): Promise<AccountResult> {
    let res: Response
    try {
        res = await apiFetch(path, {
            method,
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body)
        })
    }
    catch {
        return { success: false, message: "The account service could not be reached" }
    }

    if (res.ok) return { success: true }

    // A 429 or a gateway error may carry no JSON body.
    const data = await res.json().catch(() => null)
    return { success: false, message: data?.detail ?? "The request failed" }
}
