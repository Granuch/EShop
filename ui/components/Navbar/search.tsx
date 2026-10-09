'use client'
import React, { useEffect, useState } from 'react'
import Link from 'next/link'
import { ImageOff, Search } from 'lucide-react'

const API_URL = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:7000'

type Product = {
  id: string
  name: string
  price: number
  discountPrice: number | null
  mainImageUrl: string | null
}

type PagedResult<T> = { items: T[]; totalCount: number }

function useDebounce<T>(value: T, delay = 300) {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const t = setTimeout(() => setDebounced(value), delay)
    return () => clearTimeout(t)
  }, [value, delay])
  return debounced
}

export default function SearchBar() {
  const [input, setInput] = useState('')
  const [results, setResults] = useState<Product[]>([])
  const [loading, setLoading] = useState(false)
  const [open, setOpen] = useState(false)

  const term = useDebounce(input.trim())

  useEffect(() => {
    if (term.length < 2) {
      setResults([])
      return
    }

    const controller = new AbortController()
    setLoading(true)

    const params = new URLSearchParams({ SearchTerm: term, PageSize: '5' })

    fetch(`${API_URL}/api/v1/products?${params}`, { signal: controller.signal })
      .then((res) => {
        if (!res.ok) throw new Error(`Request failed: ${res.status}`)
        return res.json() as Promise<PagedResult<Product>>
      })
      .then((data) => setResults(data.items))
      .catch((err) => {
        if (err.name !== 'AbortError') console.error(err)
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false)
      })

    return () => controller.abort() // cancels the previous request
  }, [term])

  return (
    <form
      action="/search"
      role="search"
      className="relative order-3 w-full md:order-0 md:mx-auto md:max-w-xl md:flex-1"
      onSubmit={(e) => e.preventDefault()}
      onBlur={(e) => {
        if (!e.currentTarget.contains(e.relatedTarget)) setOpen(false)
      }}
    >
      <Search
        aria-hidden
        className="pointer-events-none absolute left-4 top-1/2 size-5 -translate-y-1/2 text-muted-foreground"
      />
      <input
        type="search"
        name="q"
        placeholder="Search products"
        aria-label="Search products"
        autoComplete="off"
        className="h-11 w-full rounded-full bg-muted pl-12 pr-4 text-sm outline-none placeholder:text-muted-foreground focus-visible:ring-2 focus-visible:ring-ring"
        value={input}
        onChange={(e) => {
          setInput(e.target.value)
          setOpen(true)
        }}
        onFocus={() => setOpen(true)}
      />

      {open && term.length >= 2 && (
        <ul className="absolute left-0 right-0 top-full z-50 mt-2 overflow-hidden rounded-xl border bg-background shadow-lg">
          {loading && <li className="px-4 py-3 text-sm text-muted-foreground">Searching…</li>}

          {!loading && results.length === 0 && (
            <li className="px-4 py-3 text-sm text-muted-foreground">No products found</li>
          )}

          {!loading &&
    results.map((p) => (
        <li key={p.id}>
      <Link
        href={`/product/${p.id}`}
        onClick={() => setOpen(false)}
        className="flex items-center gap-3 px-4 py-2 text-sm hover:bg-muted"
      >
        {p.mainImageUrl ? (
          <img
            src={p.mainImageUrl}
            alt={p.name}
            loading="lazy"
            className="size-10 shrink-0 rounded-md object-cover"
          />
        ) : (
          <div
            aria-hidden
            className="flex size-10 shrink-0 items-center justify-center rounded-md bg-muted text-xs text-muted-foreground"
          >
            <ImageOff className="size-4" />
          </div>
        )}

        <span className="min-w-0 flex-1 truncate">{p.name}</span>

        <span className="shrink-0 text-muted-foreground">
          ${p.discountPrice ?? p.price}
        </span>
      </Link>
        </li>
        ))}
        </ul>
      )}
    </form>
  )
}