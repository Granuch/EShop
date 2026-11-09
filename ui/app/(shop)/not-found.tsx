import Link from "next/link";

export default function ShopNotFound() {
  return (
    <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
      <div className="mt-20 text-center">
        <h1 className="text-2xl font-semibold tracking-tight">Page not found</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          This page doesn&apos;t exist or is no longer available.
        </p>
        <Link href="/" className="mt-2 inline-block text-sm underline">
          Back to the shop
        </Link>
      </div>
    </div>
  );
}
