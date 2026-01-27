# 🎭 End-to-End Testing Guide

E2E tests для критичних user flows з використанням Playwright.

---

## What to E2E Test

✅ **Test** (Happy Paths):
- User registration & login
- Product search & browse
- Add to cart → Checkout → Payment
- Order tracking
- Admin: Create/update product

❌ **Don't Test**:
- Edge cases (use unit/integration tests)
- Performance (use K6)
- All UI components (use Storybook)

**Coverage**: 10-20 critical scenarios (5% of total tests)

---

## Setup Playwright

### Installation

```bash
cd frontend
npm install --save-dev @playwright/test
npx playwright install
```

### Configuration

**playwright.config.ts**:

```typescript
import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests/e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: 'html',
  
  use: {
    baseURL: 'http://localhost:3000',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },

  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
    {
      name: 'firefox',
      use: { ...devices['Desktop Firefox'] },
    },
    {
      name: 'webkit',
      use: { ...devices['Desktop Safari'] },
    },
    {
      name: 'mobile',
      use: { ...devices['iPhone 13'] },
    },
  ],

  webServer: {
    command: 'npm run dev',
    url: 'http://localhost:3000',
    reuseExistingServer: !process.env.CI,
  },
});
```

---

## Test Examples

### 1. User Registration & Login

**tests/e2e/auth.spec.ts**:

```typescript
import { test, expect } from '@playwright/test';

test.describe('Authentication', () => {
  test('should register new user', async ({ page }) => {
    // Navigate to registration page
    await page.goto('/register');

    // Fill form
    await page.fill('input[name="firstName"]', 'John');
    await page.fill('input[name="lastName"]', 'Doe');
    await page.fill('input[name="email"]', `test-${Date.now()}@example.com`);
    await page.fill('input[name="password"]', 'SecureP@ss123');
    await page.fill('input[name="confirmPassword"]', 'SecureP@ss123');

    // Submit
    await page.click('button[type="submit"]');

    // Verify success
    await expect(page).toHaveURL('/');
    await expect(page.locator('nav')).toContainText('John Doe');
  });

  test('should login with valid credentials', async ({ page }) => {
    // Navigate to login
    await page.goto('/login');

    // Fill credentials
    await page.fill('input[name="email"]', 'test@example.com');
    await page.fill('input[name="password"]', 'Test123!');

    // Submit
    await page.click('button[type="submit"]');

    // Verify logged in
    await expect(page).toHaveURL('/');
    await expect(page.locator('nav')).toContainText('My Account');
  });

  test('should show error with invalid credentials', async ({ page }) => {
    await page.goto('/login');

    await page.fill('input[name="email"]', 'test@example.com');
    await page.fill('input[name="password"]', 'WrongPassword');
    await page.click('button[type="submit"]');

    // Verify error message
    await expect(page.locator('.error-message')).toContainText('Invalid credentials');
    await expect(page).toHaveURL('/login');
  });

  test('should logout', async ({ page }) => {
    // Login first
    await loginAsUser(page);

    // Logout
    await page.click('button:has-text("Logout")');

    // Verify logged out
    await expect(page).toHaveURL('/');
    await expect(page.locator('nav')).toContainText('Login');
  });
});

// Helper function
async function loginAsUser(page: Page) {
  await page.goto('/login');
  await page.fill('input[name="email"]', 'test@example.com');
  await page.fill('input[name="password"]', 'Test123!');
  await page.click('button[type="submit"]');
  await page.waitForURL('/');
}
```

---

### 2. Product Search & Browse

**tests/e2e/products.spec.ts**:

```typescript
test.describe('Product Search', () => {
  test('should search for products', async ({ page }) => {
    await page.goto('/');

    // Search
    await page.fill('input[placeholder="Search products..."]', 'laptop');
    await page.press('input[placeholder="Search products..."]', 'Enter');

    // Verify results
    await expect(page.locator('.product-card')).toHaveCount(greaterThan(0));
    await expect(page.locator('.product-card').first()).toContainText('laptop', { ignoreCase: true });
  });

  test('should filter by category', async ({ page }) => {
    await page.goto('/products');

    // Click category
    await page.click('text=Electronics');

    // Verify URL
    await expect(page).toHaveURL(/categoryId=/);

    // Verify products
    const products = page.locator('.product-card');
    await expect(products).toHaveCount(greaterThan(0));
  });

  test('should filter by price range', async ({ page }) => {
    await page.goto('/products');

    // Set price range
    await page.fill('input[name="minPrice"]', '100');
    await page.fill('input[name="maxPrice"]', '500');
    await page.click('button:has-text("Apply Filters")');

    // Verify products in range
    const prices = await page.locator('.product-price').allTextContents();
    prices.forEach(priceText => {
      const price = parseFloat(priceText.replace('$', ''));
      expect(price).toBeGreaterThanOrEqual(100);
      expect(price).toBeLessThanOrEqual(500);
    });
  });

  test('should view product details', async ({ page }) => {
    await page.goto('/products');

    // Click first product
    const firstProduct = page.locator('.product-card').first();
    const productName = await firstProduct.locator('h3').textContent();
    await firstProduct.click();

    // Verify details page
    await expect(page.locator('h1')).toContainText(productName!);
    await expect(page.locator('.product-description')).toBeVisible();
    await expect(page.locator('button:has-text("Add to Cart")')).toBeVisible();
  });
});
```

---

### 3. Shopping Cart & Checkout

**tests/e2e/checkout.spec.ts**:

```typescript
test.describe('Checkout Flow', () => {
  test('should complete full checkout process', async ({ page }) => {
    // Login
    await loginAsUser(page);

    // Browse products
    await page.goto('/products');

    // Add product to cart
    const firstProduct = page.locator('.product-card').first();
    await firstProduct.locator('button:has-text("Add to Cart")').click();

    // Verify toast notification
    await expect(page.locator('.toast-success')).toContainText('Added to cart');

    // Go to cart
    await page.click('a[href="/cart"]');

    // Verify product in cart
    await expect(page.locator('.cart-item')).toHaveCount(1);

    // Update quantity
    await page.fill('input[type="number"]', '2');
    await expect(page.locator('.cart-total')).toContainText('$', { timeout: 2000 });

    // Proceed to checkout
    await page.click('button:has-text("Proceed to Checkout")');

    // Fill shipping address
    await page.fill('input[name="address"]', '123 Test Street');
    await page.fill('input[name="city"]', 'Test City');
    await page.fill('input[name="zipCode"]', '12345');
    await page.selectOption('select[name="country"]', 'US');

    // Continue to payment
    await page.click('button:has-text("Continue to Payment")');

    // Fill payment details (test mode)
    await page.fill('input[name="cardNumber"]', '4242424242424242');
    await page.fill('input[name="expiry"]', '12/25');
    await page.fill('input[name="cvc"]', '123');

    // Place order
    await page.click('button:has-text("Place Order")');

    // Verify success
    await expect(page).toHaveURL(/\/order\/.*\/success/);
    await expect(page.locator('h1')).toContainText('Order Confirmed');
    
    // Verify order number
    const orderNumber = await page.locator('.order-number').textContent();
    expect(orderNumber).toMatch(/ORD-\d+/);
  });

  test('should remove item from cart', async ({ page }) => {
    await loginAsUser(page);

    // Add product
    await page.goto('/products');
    await page.locator('.product-card').first().locator('button:has-text("Add to Cart")').click();

    // Go to cart
    await page.goto('/cart');

    // Remove item
    await page.click('button:has-text("Remove")');

    // Verify empty cart
    await expect(page.locator('.empty-cart-message')).toContainText('Your cart is empty');
  });

  test('should apply discount code', async ({ page }) => {
    await loginAsUser(page);

    // Add product
    await page.goto('/products');
    await page.locator('.product-card').first().locator('button:has-text("Add to Cart")').click();

    // Go to cart
    await page.goto('/cart');

    // Apply discount
    await page.fill('input[name="discountCode"]', 'SAVE10');
    await page.click('button:has-text("Apply")');

    // Verify discount applied
    await expect(page.locator('.discount-amount')).toContainText('-$');
  });
});
```

---

### 4. Admin: Product Management

**tests/e2e/admin.spec.ts**:

```typescript
test.describe('Admin - Product Management', () => {
  test.beforeEach(async ({ page }) => {
    // Login as admin
    await loginAsAdmin(page);
  });

  test('should create new product', async ({ page }) => {
    // Navigate to admin products
    await page.goto('/admin/products');

    // Click create
    await page.click('button:has-text("Create Product")');

    // Fill form
    await page.fill('input[name="name"]', `Test Product ${Date.now()}`);
    await page.fill('textarea[name="description"]', 'Test description');
    await page.fill('input[name="sku"]', `SKU-${Date.now()}`);
    await page.fill('input[name="price"]', '99.99');
    await page.fill('input[name="stockQuantity"]', '10');
    await page.selectOption('select[name="categoryId"]', { index: 1 });

    // Upload image
    await page.setInputFiles('input[type="file"]', 'tests/fixtures/product-image.jpg');

    // Submit
    await page.click('button:has-text("Create Product")');

    // Verify success
    await expect(page.locator('.toast-success')).toContainText('Product created');
    await expect(page).toHaveURL('/admin/products');
  });

  test('should update product', async ({ page }) => {
    await page.goto('/admin/products');

    // Click edit on first product
    await page.locator('.product-row').first().locator('button:has-text("Edit")').click();

    // Update name
    const newName = `Updated Product ${Date.now()}`;
    await page.fill('input[name="name"]', newName);

    // Submit
    await page.click('button:has-text("Save Changes")');

    // Verify
    await expect(page.locator('.toast-success')).toContainText('Product updated');
    await expect(page.locator('.product-row').first()).toContainText(newName);
  });

  test('should delete product', async ({ page }) => {
    await page.goto('/admin/products');

    // Click delete
    await page.locator('.product-row').first().locator('button:has-text("Delete")').click();

    // Confirm dialog
    await page.click('button:has-text("Confirm")');

    // Verify
    await expect(page.locator('.toast-success')).toContainText('Product deleted');
  });
});

async function loginAsAdmin(page: Page) {
  await page.goto('/login');
  await page.fill('input[name="email"]', 'admin@example.com');
  await page.fill('input[name="password"]', 'Admin123!');
  await page.click('button[type="submit"]');
  await page.waitForURL('/admin');
}
```

---

## Page Object Model (Advanced)

### Page Class

**pages/LoginPage.ts**:

```typescript
import { Page, Locator } from '@playwright/test';

export class LoginPage {
  readonly page: Page;
  readonly emailInput: Locator;
  readonly passwordInput: Locator;
  readonly submitButton: Locator;
  readonly errorMessage: Locator;

  constructor(page: Page) {
    this.page = page;
    this.emailInput = page.locator('input[name="email"]');
    this.passwordInput = page.locator('input[name="password"]');
    this.submitButton = page.locator('button[type="submit"]');
    this.errorMessage = page.locator('.error-message');
  }

  async goto() {
    await this.page.goto('/login');
  }

  async login(email: string, password: string) {
    await this.emailInput.fill(email);
    await this.passwordInput.fill(password);
    await this.submitButton.click();
  }
}
```

### Usage

```typescript
import { LoginPage } from './pages/LoginPage';

test('should login', async ({ page }) => {
  const loginPage = new LoginPage(page);
  
  await loginPage.goto();
  await loginPage.login('test@example.com', 'Test123!');
  
  await expect(page).toHaveURL('/');
});
```

---

## Test Data Management

### Fixtures

**tests/fixtures/products.json**:

```json
{
  "validProduct": {
    "name": "Test Product",
    "sku": "TEST-001",
    "price": 99.99,
    "stockQuantity": 10
  },
  "testUser": {
    "email": "test@example.com",
    "password": "Test123!"
  }
}
```

### Usage

```typescript
import testData from './fixtures/products.json';

test('should create product', async ({ page }) => {
  await page.fill('input[name="name"]', testData.validProduct.name);
  await page.fill('input[name="price"]', testData.validProduct.price.toString());
});
```

---

## Visual Testing (Snapshots)

```typescript
test('should match product card snapshot', async ({ page }) => {
  await page.goto('/products');
  
  const productCard = page.locator('.product-card').first();
  
  await expect(productCard).toHaveScreenshot('product-card.png');
});
```

---

## Mobile Testing

```typescript
test.use({ viewport: { width: 375, height: 667 } }); // iPhone SE

test('should work on mobile', async ({ page }) => {
  await page.goto('/');
  
  // Open mobile menu
  await page.click('button[aria-label="Menu"]');
  
  await expect(page.locator('.mobile-menu')).toBeVisible();
});
```

---

## Best Practices

✅ **Do**:
- Test critical user flows only
- Use Page Object Model for complex pages
- Use data-testid for stable selectors
- Take screenshots on failure
- Run in multiple browsers

❌ **Don't**:
- Test every UI component
- Use flaky selectors (nth-child)
- Share state between tests
- Test API directly (use integration tests)
- Ignore slow tests (> 30s)

---

## Running E2E Tests

```bash
# Run all tests
npx playwright test

# Run specific test
npx playwright test tests/e2e/checkout.spec.ts

# Run in headed mode (see browser)
npx playwright test --headed

# Run in specific browser
npx playwright test --project=firefox

# Debug mode
npx playwright test --debug

# Generate report
npx playwright show-report
```

---

## CI/CD Integration

**GitHub Actions**:

```yaml
name: E2E Tests

on: [push]

jobs:
  e2e:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
      
      - name: Install dependencies
        run: npm ci
      
      - name: Install Playwright
        run: npx playwright install --with-deps
      
      - name: Run E2E tests
        run: npx playwright test
      
      - name: Upload report
        if: always()
        uses: actions/upload-artifact@v3
        with:
          name: playwright-report
          path: playwright-report/
```

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
