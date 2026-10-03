// Copied from docs/01-overview/frontend/catalog.md (TypeScript). Keep in step with that file.

// ---- Enums ----

/** Sent as its name. Query filters take the name in any case and refuse a number. */
export type ProductStatus = 'Draft' | 'Active' | 'Discontinued';

/** Request-only: the sortBy value of GET /api/v1/products and the export. Any case works. */
export type ProductSortBy = 'Name' | 'Price' | 'CreatedAt';

// ---- Queries ----

export interface ProductListQuery {
  pageNumber?: number;
  pageSize?: number;
  /** 2-200 characters; matches the name or the SKU, case-insensitive. */
  searchTerm?: string;
  /** The category and all its subcategories, at any depth. */
  categoryId?: string;
  /** Compared with the effective price, discountPrice ?? price. */
  minPrice?: number;
  maxPrice?: number;
  sortBy?: ProductSortBy;
  isDescending?: boolean;
  /** Narrows the visibility rule; never widens it. */
  status?: ProductStatus;
  hasDiscount?: boolean;
  /** Strictly less than. */
  stockBelow?: number;
  createdFrom?: string;
  createdTo?: string;
}

export interface LowStockQuery {
  /** Strictly less than. Defaults to 10; use 1 for "out of stock". Must be > 0. */
  threshold?: number;
  pageNumber?: number;
  pageSize?: number;
  categoryId?: string;
}

// ---- Responses ----

/** A list item (ProductDto). */
export interface Product {
  id: string;
  name: string;
  description: string | null;
  sku: string;
  price: number;
  /** Below price when set. Charge and show discountPrice ?? price. */
  discountPrice: number | null;
  stockQuantity: number;
  status: ProductStatus;
  categoryId: string;
  mainImageUrl: string | null;
  createdAt: string;
}

export interface ProductImage {
  id: string;
  url: string;
  altText: string | null;
  displayOrder: number;
  isMain: boolean;
}

export interface ProductAttribute {
  id: string;
  name: string;
  value: string;
}

/** GET /api/v1/products/{id} (ProductDetailsDto). */
export interface ProductDetails extends Product {
  /** By displayOrder, then age. The main image is not necessarily first. */
  images: ProductImage[];
  /** No guaranteed order. */
  attributes: ProductAttribute[];
}

export interface Category {
  id: string;
  name: string;
  description: string | null;
  slug: string;
  parentCategoryId: string | null;
  parentCategoryName: string | null;
  displayOrder: number;
  /** false only in an admin's tree. */
  isActive: boolean;
  /** The full subtree, at any depth. [] means no visible children. */
  childCategories: Category[];
}

// ---- Admin: products ----

export interface CreatedResourceResponse {
  id: string;
}

export interface ProductStockResponse {
  productId: string;
  stockQuantity: number;
}

export interface CreateProductImage {
  /** Absolute http(s) URL, at most 500 characters. */
  url: string;
  altText?: string | null;
  displayOrder?: number;
}

/** One attribute: add, update, and an item of create and replace. */
export interface AttributeInput {
  name: string;
  value: string;
}

export interface CreateProductRequest {
  name: string;
  /** ^[A-Za-z0-9_-]+$, at most 50, case-sensitive. */
  sku: string;
  price: number;
  stockQuantity: number;
  categoryId: string;
  description?: string | null;
  /** At most 10. The first one becomes the main image. */
  images?: CreateProductImage[] | null;
  /** At most 50, names unique ignoring case. */
  attributes?: AttributeInput[] | null;
}

export interface UpdateProductRequest {
  /** Must equal the {id} in the route. */
  productId: string;
  price: number;
  /** Replaces the stock. Prefer PATCH /stock for movements. */
  stockQuantity: number;
  name?: string | null;
  /** Omitted or null keeps it; "" clears it. */
  description?: string | null;
  sku?: string | null;
  categoryId?: string | null;
}

/** Send exactly one of delta and absolute. */
export type AdjustStockRequest =
  | { delta: number; absolute?: never; reason?: string | null }
  | { absolute: number; delta?: never; reason?: string | null };

export interface SetDiscountRequest {
  /** Greater than 0 and below the product's price. */
  discountPrice: number;
}

export interface AddImageRequest {
  url: string;
  altText?: string | null;
  displayOrder?: number;
}

/** Both fields replace the stored values: an omitted altText clears it. */
export interface UpdateImageRequest {
  url: string;
  altText?: string | null;
}

export interface ReorderImagesRequest {
  /** Every image id of the product, once each, in the new order. */
  imageIds: string[];
}

export interface ReplaceAttributesRequest {
  /** The complete new set. [] removes every attribute. */
  attributes: AttributeInput[];
}

export interface DeletedProductsQuery {
  pageNumber?: number;
  pageSize?: number;
  categoryId?: string;
  searchTerm?: string;
}
