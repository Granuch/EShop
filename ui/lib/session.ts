import { cookies } from "next/headers";
import { apiFetch } from "./api";

export async function getSession() {
    const accessToken = (await cookies()).get("access_token")?.value
    if(!accessToken) return null;

    const res = await apiFetch("/api/v1/account/profile")
    if(!res.ok) return null;
    return res.json()
}