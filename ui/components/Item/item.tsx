import React from "react";
import Image from "next/image";
import Link from "next/link";
import { itemData } from "./types/itemType";

type itemProp = {
  itemData: itemData;
};

const FALLBACK_IMAGE = "/image-not-found-failure-network-260nw-2330163829.webp";

function Item({ itemData }: itemProp) {
  return (
    <Link
      href={`/product/${itemData.id}`}
      className="group block rounded-lg outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
    >
      {/* Fixed 3:4 ratio keeps every card the same height at any grid width */}
      <div className="relative aspect-3/4 overflow-hidden rounded-lg bg-muted">
        <Image
          src={itemData.mainImageUrl || FALLBACK_IMAGE}
          alt={itemData.name}
          fill
          sizes="(min-width: 1280px) 240px, (min-width: 768px) 33vw, 50vw"
          className="object-cover motion-safe:transition-transform motion-safe:duration-300 motion-safe:group-hover:scale-105"
        />
      </div>

      <div className="mt-3 space-y-1">
        <h3 className="line-clamp-1 font-medium group-hover:underline">
          {itemData.name}
        </h3>
        <div>
          <p className={`text-sm text-gray-400 ${itemData.discountPrice && "line-through"}`}>${itemData.price}</p>
          {itemData.discountPrice && <p>${itemData.discountPrice}</p>}
        </div>
      </div>
    </Link>
  );
}

export default Item;