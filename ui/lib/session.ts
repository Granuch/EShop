import { cookies } from "next/headers";
import { apiFetch } from "./api";

export type UserProfile = {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  profilePictureUrl: string | null;
  emailConfirmed: boolean;
  twoFactorEnabled: boolean;
  isActive: boolean;
  createdAt: string;
  lastLoginAt: string | null;
  roles: string[];
}

export async function getSession() {
    const accessToken = (await cookies()).get("access_token")?.value
    if(!accessToken) return null;

    const res = await apiFetch("/api/v1/account/profile")
    if(!res.ok) return null;
    return res.json()
}