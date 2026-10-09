export async function clientFetch(url:string, options:RequestInit = {}) {
    let res = await fetch(url, options)
    if(res.status === 401) {
        const refreshRes = await fetch("/api/auth/refresh", {
            method: "POST"
        })
        if(refreshRes.ok) {
            res = await fetch(url, options)
        } else {
            window.location.href = "/login"
        }
    }

    return res
}