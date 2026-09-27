import { cookies } from "next/headers";

export async function apiFetch(path:string, options:RequestInit = {}) {
    const cookieStore = await cookies()
    let accessToken = cookieStore.get("access_token")?.value

    const doFetch = (token?: string) => {
        return fetch(`http://localhost:7000${path}`, {
            ...options,
            headers: {...options.headers, Authorization: `Bearer ${token}`}
        })
    }

    let res: Response = await doFetch(accessToken)

    if(res.status === 401) {
        const refreshed = await fetch("http://localhost:7000/api/auth/refresh", {
            method: "POST",
            headers: { cookie: cookieStore.toString() }
        })

        if(refreshed.ok) {
            accessToken = (await cookies()).get("access_token")?.value
            res = await doFetch(accessToken)
        }
    }

    return res    
}
