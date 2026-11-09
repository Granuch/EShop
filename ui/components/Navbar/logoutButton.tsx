'use client'
import { useRouter } from 'next/navigation'
import React from 'react'

function LogoutButton() { 
    const router = useRouter()

    async function handleButton() {
    
        await fetch("/api/auth/logout", {
            method: "POST",
            headers: { "Content-Type": "application/json" }
        })

        router.push("/")
        router.refresh()
  }

    return (
    <button onClick={handleButton} className="text-red-500 hover:cursor-pointer">Logout</button>
  )
}

export default LogoutButton