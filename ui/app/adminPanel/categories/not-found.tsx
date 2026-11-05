import Link from "next/link";

export default function CategoryNotFound() {
  return (
    <div className="mx-auto max-w-md py-20 text-center">
      <h1 className="text-2xl font-semibold tracking-tight">Category not found</h1>
      <p className="mt-1 text-sm text-muted-foreground">It does not exist. Deleted categories are still in the tree.</p>
      <Link href="/adminPanel/categories" className="mt-4 inline-block text-sm underline">
        Back to categories
      </Link>
    </div>
  );
}
