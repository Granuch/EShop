import Image from "next/image";
import Item from "@/components/Item/item";
import { itemData } from "@/components/Item/types/itemType";

async function fetchProducts(): Promise<itemData[]> {
  const res = await fetch("http://localhost:7000/api/v1/products")
  if(!res.ok) throw new Error("Failed to fetch products");
  
  const data = await res.json()
  return data.items
}

export default async function Home() {

  const products = await fetchProducts()

  return (
    <div className="flex gap-6">
      <div className="">

      </div>
      <div className="grid grid-cols-[repeat(auto-fill,300px)] max-w-full mx-auto gap-7 mt-10">
        {products.map((product) => (<Item key={product.id} itemData={product}/>))}
      </div>
    </div>
  );
}
