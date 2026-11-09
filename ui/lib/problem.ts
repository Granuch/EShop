// The gateway's error envelope (problem+json), as far as the UI reads it. A route handler's own failures use `error`.
export type Problem = {
    detail?: string
    errorCode?: string
    errors?: Record<string, string[]>
    error?: string
} | null

/** Every message a failed call should show: one per field error, otherwise the detail. */
export function problemMessages(status: number, data: Problem, retryAfter: string | null, fallback: string): string[] {
    if (status === 429) {
        const seconds = Number(retryAfter)
        return [Number.isFinite(seconds) && seconds > 0
            ? `Too many attempts. Try again in ${seconds} seconds.`
            : "Too many attempts. Try again in a minute."]
    }
    const fieldMessages = data?.errors ? Object.values(data.errors).flat() : []
    if (fieldMessages.length > 0) return fieldMessages
    return [data?.detail ?? data?.error ?? fallback]
}

export async function readProblem(res: Response): Promise<Problem> {
    // A 429 or a gateway error may carry no JSON body.
    return res.json().catch(() => null)
}
