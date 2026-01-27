# ⚛️ Phase 8: Frontend Implementation (React)

**Duration**: 3 weeks  
**Team Size**: 2-3 frontend developers  
**Prerequisites**: All backend services (Phases 2-7) completed  
**Status**: 📋 Planning

---

## Objectives

- ✅ React SPA with TypeScript
- ✅ Product catalog & search
- ✅ Shopping basket
- ✅ User authentication
- ✅ Checkout flow
- ✅ Order tracking
- ✅ Admin panel
- ✅ Responsive design (mobile-first)

---

## Tech Stack

| Technology | Purpose |
|------------|---------|
| **React 18** | UI framework |
| **TypeScript** | Type safety |
| **Vite** | Build tool |
| **React Router** | Routing |
| **TanStack Query** | Data fetching |
| **Zustand** | State management |
| **Ant Design / Tailwind CSS** | UI components |
| **Axios** | HTTP client |
| **React Hook Form** | Form management |
| **Zod** | Validation |

---

## Tasks Breakdown

### 8.1 Project Setup

**Estimated Time**: 0.5 day

```bash
# Create React app with Vite
npm create vite@latest eshop-web -- --template react-ts

cd eshop-web

# Install dependencies
npm install react-router-dom @tanstack/react-query axios zustand
npm install react-hook-form zod @hookform/resolvers
npm install antd tailwindcss

# Setup Tailwind
npx tailwindcss init -p
```

**Project Structure:**

```
src/
├── api/               # API clients
│   ├── authApi.ts
│   ├── productsApi.ts
│   ├── basketApi.ts
│   └── ordersApi.ts
├── components/
│   ├── common/        # Reusable components
│   ├── layout/        # Layout components
│   └── features/      # Feature-specific components
├── pages/
│   ├── Home.tsx
│   ├── Products.tsx
│   ├── ProductDetail.tsx
│   ├── Basket.tsx
│   ├── Checkout.tsx
│   ├── Orders.tsx
│   └── Login.tsx
├── hooks/             # Custom hooks
├── store/             # Zustand stores
├── types/             # TypeScript types
├── utils/             # Utilities
└── App.tsx
```

---

### 8.2 Authentication & Authorization

**Estimated Time**: 2 days

**Auth Store (Zustand):**

```tsx
// src/store/authStore.ts

interface AuthState {
  user: User | null;
  accessToken: string | null;
  login: (email: string, password: string) => Promise<void>;
  logout: () => void;
  refreshToken: () => Promise<void>;
}

export const useAuthStore = create<AuthState>((set, get) => ({
  user: null,
  accessToken: localStorage.getItem('accessToken'),

  login: async (email, password) => {
    const response = await authApi.login({ email, password });
    
    localStorage.setItem('accessToken', response.accessToken);
    localStorage.setItem('refreshToken', response.refreshToken);
    
    set({ user: response.user, accessToken: response.accessToken });
  },

  logout: () => {
    localStorage.removeItem('accessToken');
    localStorage.removeItem('refreshToken');
    set({ user: null, accessToken: null });
  },

  refreshToken: async () => {
    const refreshToken = localStorage.getItem('refreshToken');
    if (!refreshToken) return;

    const response = await authApi.refreshToken({ refreshToken });
    localStorage.setItem('accessToken', response.accessToken);
    set({ accessToken: response.accessToken });
  }
}));
```

**Login Page:**

```tsx
// src/pages/Login.tsx

export const LoginPage = () => {
  const { login } = useAuthStore();
  const navigate = useNavigate();
  
  const { register, handleSubmit, formState: { errors } } = useForm<LoginForm>({
    resolver: zodResolver(loginSchema)
  });

  const onSubmit = async (data: LoginForm) => {
    try {
      await login(data.email, data.password);
      navigate('/');
    } catch (error) {
      toast.error('Invalid credentials');
    }
  };

  return (
    <div className="min-h-screen flex items-center justify-center">
      <form onSubmit={handleSubmit(onSubmit)} className="w-full max-w-md">
        <h2 className="text-2xl font-bold mb-6">Login</h2>
        
        <Input
          label="Email"
          type="email"
          {...register('email')}
          error={errors.email?.message}
        />
        
        <Input
          label="Password"
          type="password"
          {...register('password')}
          error={errors.password?.message}
        />
        
        <Button type="submit" className="w-full">Login</Button>
      </form>
    </div>
  );
};
```

**Protected Route:**

```tsx
// src/components/ProtectedRoute.tsx

export const ProtectedRoute = ({ children }: { children: React.ReactNode }) => {
  const { accessToken } = useAuthStore();
  
  if (!accessToken) {
    return <Navigate to="/login" replace />;
  }

  return <>{children}</>;
};
```

---

### 8.3 Product Catalog

**Estimated Time**: 3 days

**Products API Client:**

```ts
// src/api/productsApi.ts

export const productsApi = {
  getProducts: async (params: GetProductsParams) => {
    const { data } = await apiClient.get<PagedResult<Product>>('/products', { params });
    return data;
  },

  getProductById: async (id: string) => {
    const { data } = await apiClient.get<Product>(`/products/${id}`);
    return data;
  },

  searchProducts: async (searchTerm: string) => {
    const { data } = await apiClient.get<Product[]>(`/products/search`, {
      params: { q: searchTerm }
    });
    return data;
  }
};
```

**Products Page:**

```tsx
// src/pages/Products.tsx

export const ProductsPage = () => {
  const [searchParams, setSearchParams] = useSearchParams();
  const page = parseInt(searchParams.get('page') || '1');
  const category = searchParams.get('category');

  const { data, isLoading } = useQuery({
    queryKey: ['products', page, category],
    queryFn: () => productsApi.getProducts({ page, pageSize: 12, categoryId: category })
  });

  return (
    <div className="container mx-auto px-4">
      <div className="flex gap-6">
        {/* Filters Sidebar */}
        <aside className="w-64">
          <CategoryFilter />
          <PriceFilter />
        </aside>

        {/* Products Grid */}
        <main className="flex-1">
          <div className="grid grid-cols-3 gap-6">
            {data?.items.map(product => (
              <ProductCard key={product.id} product={product} />
            ))}
          </div>

          <Pagination
            current={page}
            total={data?.totalCount}
            pageSize={12}
            onChange={(page) => setSearchParams({ page: page.toString() })}
          />
        </main>
      </div>
    </div>
  );
};
```

**Product Card Component:**

```tsx
// src/components/ProductCard.tsx

export const ProductCard = ({ product }: { product: Product }) => {
  const { addItem } = useBasketStore();

  return (
    <div className="border rounded-lg p-4 hover:shadow-lg transition">
      <img
        src={product.imageUrl}
        alt={product.name}
        className="w-full h-48 object-cover rounded"
      />
      
      <h3 className="font-semibold mt-2">{product.name}</h3>
      <p className="text-gray-600 text-sm">{product.description}</p>
      
      <div className="flex items-center justify-between mt-4">
        <span className="text-xl font-bold">${product.price}</span>
        <Button
          onClick={() => addItem(product)}
          disabled={product.stockQuantity === 0}
        >
          {product.stockQuantity > 0 ? 'Add to Cart' : 'Out of Stock'}
        </Button>
      </div>
    </div>
  );
};
```

---

### 8.4 Shopping Basket

**Estimated Time**: 2 days

**Basket Store:**

```tsx
// src/store/basketStore.ts

interface BasketState {
  items: BasketItem[];
  addItem: (product: Product, quantity?: number) => void;
  removeItem: (productId: string) => void;
  updateQuantity: (productId: string, quantity: number) => void;
  clearBasket: () => void;
  totalAmount: number;
}

export const useBasketStore = create<BasketState>((set, get) => ({
  items: [],

  addItem: (product, quantity = 1) => {
    const { items } = get();
    const existingItem = items.find(i => i.productId === product.id);

    if (existingItem) {
      set({
        items: items.map(i =>
          i.productId === product.id
            ? { ...i, quantity: i.quantity + quantity }
            : i
        )
      });
    } else {
      set({
        items: [...items, {
          productId: product.id,
          productName: product.name,
          price: product.price,
          quantity,
          imageUrl: product.imageUrl
        }]
      });
    }

    // Sync with backend
    basketApi.addItem(product.id, quantity);
  },

  removeItem: (productId) => {
    set({ items: get().items.filter(i => i.productId !== productId) });
    basketApi.removeItem(productId);
  },

  updateQuantity: (productId, quantity) => {
    if (quantity <= 0) {
      get().removeItem(productId);
    } else {
      set({
        items: get().items.map(i =>
          i.productId === productId ? { ...i, quantity } : i
        )
      });
      basketApi.updateQuantity(productId, quantity);
    }
  },

  clearBasket: () => {
    set({ items: [] });
    basketApi.clearBasket();
  },

  get totalAmount() {
    return get().items.reduce((sum, item) => sum + item.price * item.quantity, 0);
  }
}));
```

**Basket Page:**

```tsx
export const BasketPage = () => {
  const { items, removeItem, updateQuantity, totalAmount } = useBasketStore();
  const navigate = useNavigate();

  if (items.length === 0) {
    return (
      <div className="text-center py-12">
        <h2>Your basket is empty</h2>
        <Button onClick={() => navigate('/products')}>Continue Shopping</Button>
      </div>
    );
  }

  return (
    <div className="container mx-auto px-4">
      <h1 className="text-3xl font-bold mb-6">Shopping Basket</h1>

      <div className="space-y-4">
        {items.map(item => (
          <div key={item.productId} className="flex items-center gap-4 border p-4 rounded">
            <img src={item.imageUrl} className="w-24 h-24 object-cover rounded" />
            
            <div className="flex-1">
              <h3 className="font-semibold">{item.productName}</h3>
              <p className="text-gray-600">${item.price}</p>
            </div>

            <InputNumber
              min={1}
              value={item.quantity}
              onChange={(value) => updateQuantity(item.productId, value!)}
            />

            <p className="font-bold">${item.price * item.quantity}</p>

            <Button onClick={() => removeItem(item.productId)} danger>Remove</Button>
          </div>
        ))}
      </div>

      <div className="mt-8 text-right">
        <h3 className="text-2xl font-bold">Total: ${totalAmount.toFixed(2)}</h3>
        <Button size="large" type="primary" onClick={() => navigate('/checkout')}>
          Proceed to Checkout
        </Button>
      </div>
    </div>
  );
};
```

---

### 8.5 Checkout Flow

**Estimated Time**: 2 days

```tsx
export const CheckoutPage = () => {
  const { items, totalAmount, clearBasket } = useBasketStore();
  const navigate = useNavigate();
  const { mutate: checkout } = useMutation({
    mutationFn: basketApi.checkout,
    onSuccess: (orderId) => {
      clearBasket();
      navigate(`/orders/${orderId}`);
    }
  });

  const { register, handleSubmit } = useForm<CheckoutForm>();

  const onSubmit = (data: CheckoutForm) => {
    checkout({
      shippingAddress: data.address,
      paymentMethod: data.paymentMethod
    });
  };

  return (
    <form onSubmit={handleSubmit(onSubmit)}>
      <h1>Checkout</h1>

      <Input label="Shipping Address" {...register('address')} required />
      <Select label="Payment Method" {...register('paymentMethod')}>
        <option value="credit_card">Credit Card</option>
        <option value="paypal">PayPal</option>
      </Select>

      <div className="summary">
        <h3>Order Summary</h3>
        <p>Items: {items.length}</p>
        <p>Total: ${totalAmount}</p>
      </div>

      <Button type="submit">Place Order</Button>
    </form>
  );
};
```

---

### 8.6 Admin Panel

**Estimated Time**: 3 days

```tsx
// src/pages/admin/Products.tsx

export const AdminProductsPage = () => {
  const { data: products } = useQuery({
    queryKey: ['admin-products'],
    queryFn: productsApi.getProducts
  });

  const { mutate: deleteProduct } = useMutation({
    mutationFn: productsApi.deleteProduct,
    onSuccess: () => queryClient.invalidateQueries(['admin-products'])
  });

  return (
    <div>
      <div className="flex justify-between mb-4">
        <h1>Products Management</h1>
        <Button onClick={() => navigate('/admin/products/new')}>Add Product</Button>
      </div>

      <Table
        dataSource={products?.items}
        columns={[
          { title: 'Name', dataIndex: 'name' },
          { title: 'SKU', dataIndex: 'sku' },
          { title: 'Price', dataIndex: 'price' },
          { title: 'Stock', dataIndex: 'stockQuantity' },
          {
            title: 'Actions',
            render: (_, record) => (
              <>
                <Button onClick={() => navigate(`/admin/products/${record.id}`)}>Edit</Button>
                <Button onClick={() => deleteProduct(record.id)} danger>Delete</Button>
              </>
            )
          }
        ]}
      />
    </div>
  );
};
```

---

## Success Criteria

- [x] Users can browse and search products
- [x] Shopping basket functionality
- [x] Checkout and order placement
- [x] User authentication and profile
- [x] Admin panel for product management
- [x] Responsive design (mobile, tablet, desktop)
- [x] All pages tested (> 70% coverage)

---

## Next Phase

→ [Phase 9: Testing Strategy Implementation](phase-9-testing.md)

---

**Version**: 1.0  
**Last Updated**: 2024-01-15
