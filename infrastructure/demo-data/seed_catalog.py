"""Fills the shop with a demo catalog: about 155 products with photos, in a two-level category tree.

The products come from DummyJSON (https://dummyjson.com), a free catalog made for demos and prototypes: names,
descriptions, prices, stock, brands, SKUs and product photos on a white background. The photos stay on DummyJSON's
CDN (cdn.dummyjson.com, allowed in ui/next.config.ts); the shop's Next.js server fetches and caches them.

Everything goes through the shop's own admin API (as an admin account), so caches, events and the audit log behave
exactly as for an admin working in the panel. Safe to run again: categories are matched by slug, and a product whose
SKU already exists is skipped.

    python infrastructure/demo-data/seed_catalog.py --email admin@eshop.com --password '<admin password>'
    python infrastructure/demo-data/seed_catalog.py ... --remove-placeholders   # also delete the four seed products

Needs only the Python standard library. --base-url defaults to the gateway on this machine (http://localhost:7000).
"""
import argparse
import json
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

# DummyJSON category -> (root slug, child slug). Vehicles, motorcycles and groceries are left out on purpose.
CATEGORY_MAP = {
    "smartphones": ("electronics", "phones"),
    "laptops": ("electronics", "laptops"),
    "tablets": ("electronics", "tablets"),
    "mobile-accessories": ("electronics", "accessories"),
    "mens-shirts": ("fashion", "mens-clothing"),
    "mens-shoes": ("fashion", "mens-shoes"),
    "womens-dresses": ("fashion", "womens-clothing"),
    "tops": ("fashion", "womens-clothing"),
    "womens-shoes": ("fashion", "womens-shoes"),
    "womens-bags": ("fashion", "bags"),
    "womens-jewellery": ("fashion", "jewellery"),
    "mens-watches": ("fashion", "watches"),
    "womens-watches": ("fashion", "watches"),
    "sunglasses": ("fashion", "sunglasses"),
    "beauty": ("beauty", "makeup"),
    "fragrances": ("beauty", "fragrances"),
    "skin-care": ("beauty", "skin-care"),
    "furniture": ("home", "furniture"),
    "home-decoration": ("home", "home-decor"),
    "kitchen-accessories": ("home", "kitchen"),
    "sports-accessories": ("sports", "sports-equipment"),
}

# slug -> (name, description, display order); roots first, then their children.
ROOTS = {
    "electronics": ("Electronics", "Phones, laptops, tablets and the accessories that go with them.", 0),
    "fashion": ("Fashion", "Clothing, shoes, bags, watches and jewellery.", 1),
    "beauty": ("Beauty", "Makeup, fragrances and skin care.", 2),
    "home": ("Home & Kitchen", "Furniture, decor and everything for the kitchen.", 3),
    "sports": ("Sports", "Equipment for training and games.", 4),
}
CHILDREN = {
    "phones": ("Phones", 0), "laptops": ("Laptops", 1), "tablets": ("Tablets", 2), "accessories": ("Accessories", 3),
    "mens-clothing": ("Men's Clothing", 0), "mens-shoes": ("Men's Shoes", 1), "womens-clothing": ("Women's Clothing", 2),
    "womens-shoes": ("Women's Shoes", 3), "bags": ("Bags", 4), "jewellery": ("Jewellery", 5), "watches": ("Watches", 6),
    "sunglasses": ("Sunglasses", 7),
    "makeup": ("Makeup", 0), "fragrances": ("Fragrances", 1), "skin-care": ("Skin Care", 2),
    "furniture": ("Furniture", 0), "home-decor": ("Home Decor", 1), "kitchen": ("Kitchen", 2),
    "sports-equipment": ("Sports Equipment", 0),
}

# The four products CatalogSeedData creates on an empty database (placeholder photos), removed on request.
PLACEHOLDER_NAMES = {"Domain-Driven Design", "NovaBook Pro 14", "Phantom X12", "Phantom X12 Lite"}
PLACEHOLDER_CATEGORY_SLUGS = ["test", "test123", "books"]


class Api:
    def __init__(self, base_url, token=None):
        self.base = base_url.rstrip("/")
        self.token = token

    def call(self, method, path, body=None, ok=(200, 201, 204)):
        data = None if body is None else json.dumps(body).encode("utf-8")
        req = urllib.request.Request(self.base + path, data=data, method=method)
        req.add_header("Content-Type", "application/json")
        if self.token:
            req.add_header("Authorization", "Bearer " + self.token)
        for attempt in range(5):
            try:
                with urllib.request.urlopen(req, timeout=30) as res:
                    raw = res.read()
                    return res.status, (json.loads(raw) if raw else None)
            except urllib.error.HTTPError as e:
                raw = e.read()
                payload = json.loads(raw) if raw else None
                if e.code == 429 and attempt < 4:
                    time.sleep(int(e.headers.get("Retry-After") or 10))
                    continue
                if e.code in ok:
                    return e.code, payload
                return e.code, payload
        raise RuntimeError(f"{method} {path}: still rate limited")


def fetch_json(url):
    # DummyJSON's Cloudflare answers 403 to Python's default User-Agent.
    req = urllib.request.Request(url, headers={"User-Agent": "EShop-demo-seed/1.0", "Accept": "application/json"})
    with urllib.request.urlopen(req, timeout=60) as res:
        return json.loads(res.read())


def category_index(api):
    status, tree = api.call("GET", "/api/v1/categories")
    if status != 200:
        sys.exit(f"Could not read categories: {status} {tree}")
    roots, children = {}, {}
    for root in tree:
        roots[root["slug"]] = root["id"]
        for child in root.get("childCategories") or []:
            children[(root["slug"], child["slug"])] = child["id"]
    return roots, children, tree


def ensure_categories(api):
    roots, children, _ = category_index(api)
    for slug, (name, description, order) in ROOTS.items():
        if slug not in roots:
            status, body = api.call("POST", "/api/v1/categories",
                                    {"name": name, "slug": slug, "description": description, "displayOrder": order})
            if status != 201:
                sys.exit(f"Could not create category {slug}: {status} {body}")
            roots[slug] = body["id"]
            print(f"category {name}")
    for _, (root_slug, child_slug) in CATEGORY_MAP.items():
        if (root_slug, child_slug) in children:
            continue
        name, order = CHILDREN[child_slug]
        status, body = api.call("POST", "/api/v1/categories",
                                {"name": name, "slug": child_slug, "parentCategoryId": roots[root_slug], "displayOrder": order})
        if status != 201:
            sys.exit(f"Could not create category {child_slug}: {status} {body}")
        children[(root_slug, child_slug)] = body["id"]
        print(f"  category {name}")
    return children


def clip(text, limit):
    text = (text or "").strip()
    return text if len(text) <= limit else text[: limit - 1].rstrip() + "…"


def attributes_for(p):
    attrs = []
    if p.get("brand"):
        attrs.append({"name": "Brand", "value": clip(p["brand"], 200)})
    if p.get("weight"):
        attrs.append({"name": "Weight", "value": f"{p['weight']} kg"})
    d = p.get("dimensions") or {}
    if d:
        attrs.append({"name": "Dimensions", "value": f"{d.get('width')} × {d.get('height')} × {d.get('depth')} cm"})
    # DummyJSON's minimumOrderQuantity is left out: the basket accepts one of anything, so it would mislead.
    for key, label in (("warrantyInformation", "Warranty"), ("shippingInformation", "Shipping"),
                       ("returnPolicy", "Returns")):
        if p.get(key):
            attrs.append({"name": label, "value": clip(str(p[key]), 200)})
    return attrs


def seed_products(api, children, limit=None):
    data = fetch_json("https://dummyjson.com/products?limit=0")
    products = [p for p in data["products"] if p["category"] in CATEGORY_MAP]
    if limit:
        products = products[:limit]
    created = skipped = failed = 0
    for p in products:
        root_slug, child_slug = CATEGORY_MAP[p["category"]]
        images = [{"url": url, "altText": clip(p["title"], 200), "displayOrder": i}
                  for i, url in enumerate(dict.fromkeys(p.get("images") or [])) if url][:10]
        body = {
            "name": clip(p["title"], 200),
            "sku": clip(p.get("sku") or f"DJ-{p['id']}", 50),
            "price": round(float(p["price"]), 2),
            "stockQuantity": max(0, int(p.get("stock") or 0)),
            "categoryId": children[(root_slug, child_slug)],
            "description": clip(p.get("description"), 1000) or None,
            "images": images or None,
            "attributes": attributes_for(p) or None,
        }
        status, res = api.call("POST", "/api/v1/products", body)
        if status in (400, 409) and res and res.get("errorCode") == "Product.SkuConflict":
            skipped += 1
            continue
        if status != 201:
            failed += 1
            print(f"FAILED {p['title']}: {status} {res}")
            continue
        product_id = res["id"]
        api.call("POST", f"/api/v1/products/{product_id}/publish")
        # A real discount on about every third product, so the storefront shows crossed-out prices.
        pct = float(p.get("discountPercentage") or 0)
        if pct >= 12:
            discount = round(body["price"] * (1 - pct / 100), 2)
            if 0 < discount < body["price"]:
                api.call("PUT", f"/api/v1/products/{product_id}/discount", {"discountPrice": discount})
        created += 1
        print(f"  + {body['name']}  ${body['price']}")
        time.sleep(0.05)
    return created, skipped, failed


def remove_placeholders(api):
    # Searched by name: after seeding they are the oldest of 150+ products, past any single page.
    for name in sorted(PLACEHOLDER_NAMES):
        query = urllib.parse.urlencode({"SearchTerm": name, "PageSize": 50})
        _, page = api.call("GET", f"/api/v1/products?{query}")
        for p in (page or {}).get("items", []):
            if p["name"] == name:
                s, _ = api.call("DELETE", f"/api/v1/products/{p['id']}")
                print(f"removed product {p['name']}: {s}")
    _, _, tree = category_index(api)
    by_slug = {}
    for root in tree:
        by_slug[root["slug"]] = root["id"]
        for child in root.get("childCategories") or []:
            by_slug.setdefault(child["slug"], child["id"])
    for slug in PLACEHOLDER_CATEGORY_SLUGS:
        if slug in by_slug:
            s, body = api.call("DELETE", f"/api/v1/categories/{by_slug[slug]}")
            # 404: already removed (the category list can lag behind a delete while it is cached).
            if s != 404:
                print(f"removed category {slug}: {s}" + ("" if s == 204 else f" {body and body.get('detail')}"))


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--base-url", default="http://localhost:7000")
    ap.add_argument("--email", required=True)
    ap.add_argument("--password", required=True)
    ap.add_argument("--remove-placeholders", action="store_true")
    ap.add_argument("--limit", type=int, help="seed only the first N products (for a quick check)")
    args = ap.parse_args()

    status, login = Api(args.base_url).call("POST", "/api/v1/auth/login", {"email": args.email, "password": args.password})
    if status != 200 or not login.get("accessToken"):
        sys.exit(f"Sign-in failed: {status} {login}")
    if "catalog.write" not in (login.get("user") or {}).get("permissions", []):
        sys.exit("This account cannot manage the catalog (no catalog.write permission).")
    api = Api(args.base_url, login["accessToken"])

    children = ensure_categories(api)
    created, skipped, failed = seed_products(api, children, args.limit)
    print(f"products: {created} created, {skipped} already there, {failed} failed")
    if args.remove_placeholders:
        remove_placeholders(api)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
