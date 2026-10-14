// The emailed links carry userId and token as query parameters; Next has already URL-decoded them.
export type LinkSearchParams = Promise<Record<string, string | string[] | undefined>>

export async function readLinkParams(searchParams: LinkSearchParams): Promise<{ userId: string; token: string } | null> {
    const params = await searchParams
    const userId = typeof params.userId === "string" ? params.userId : ""
    const token = typeof params.token === "string" ? params.token : ""
    return userId && token ? { userId, token } : null
}
