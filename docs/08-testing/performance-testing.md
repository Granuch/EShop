# ⚡ Performance Testing Guide

Performance та load testing з використанням K6.

---

## Types of Performance Tests

### 1. Load Testing

**Goal**: Test system under expected load.

**Scenario**: 100 users browsing products for 5 minutes.

### 2. Stress Testing

**Goal**: Find system breaking point.

**Scenario**: Gradually increase users until system fails.

### 3. Spike Testing

**Goal**: Test system resilience to sudden traffic spikes.

**Scenario**: Sudden jump from 10 → 1000 users.

### 4. Soak Testing (Endurance)

**Goal**: Test system stability over long period.

**Scenario**: 100 users for 2 hours (find memory leaks).

---

## K6 Setup

### Installation

```bash
# Windows (Chocolatey)
choco install k6

# macOS (Homebrew)
brew install k6

# Linux
sudo gpg -k
sudo gpg --no-default-keyring --keyring /usr/share/keyrings/k6-archive-keyring.gpg --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys C5AD17C747E3415A3642D57D77C6C491D6AC1D69
echo "deb [signed-by=/usr/share/keyrings/k6-archive-keyring.gpg] https://dl.k6.io/deb stable main" | sudo tee /etc/apt/sources.list.d/k6.list
sudo apt-get update
sudo apt-get install k6

# Docker
docker pull grafana/k6:latest
```

---

## Basic Load Test

**tests/performance/load-test.js**:

```javascript
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate } from 'k6/metrics';

// Custom metrics
const errorRate = new Rate('errors');

// Test configuration
export const options = {
  stages: [
    { duration: '1m', target: 50 },   // Ramp up to 50 users
    { duration: '3m', target: 100 },  // Stay at 100 users
    { duration: '1m', target: 0 },    // Ramp down to 0
  ],
  thresholds: {
    http_req_duration: ['p(95)<500'],        // 95% of requests < 500ms
    http_req_failed: ['rate<0.01'],          // Error rate < 1%
    errors: ['rate<0.1'],                    // Custom error rate < 10%
  },
};

const BASE_URL = __ENV.BASE_URL || 'https://api.eshop.com';

export default function () {
  // Homepage
  let res = http.get(`${BASE_URL}/api/v1/products?page=1&pageSize=20`);
  
  check(res, {
    'status is 200': (r) => r.status === 200,
    'response time < 500ms': (r) => r.timings.duration < 500,
    'has products': (r) => JSON.parse(r.body).items.length > 0,
  }) || errorRate.add(1);

  sleep(1); // Think time (user reads page)

  // Product details
  const products = JSON.parse(res.body).items;
  if (products.length > 0) {
    const randomProduct = products[Math.floor(Math.random() * products.length)];
    
    res = http.get(`${BASE_URL}/api/v1/products/${randomProduct.id}`);
    
    check(res, {
      'product details status 200': (r) => r.status === 200,
    }) || errorRate.add(1);
  }

  sleep(2);
}
```

### Run

```bash
# Run locally
k6 run tests/performance/load-test.js

# Run with custom environment
k6 run --env BASE_URL=https://staging-api.eshop.com tests/performance/load-test.js

# Run with specific VUs
k6 run --vus 100 --duration 5m tests/performance/load-test.js
```

---

## Stress Test (Find Breaking Point)

**tests/performance/stress-test.js**:

```javascript
export const options = {
  stages: [
    { duration: '2m', target: 100 },   // Normal load
    { duration: '5m', target: 200 },   // Around breaking point
    { duration: '2m', target: 300 },   // Beyond breaking point
    { duration: '5m', target: 400 },   // Further beyond
    { duration: '5m', target: 0 },     // Ramp down
  ],
};

export default function () {
  const res = http.get(`${BASE_URL}/api/v1/products`);
  
  check(res, {
    'status 200 or 503': (r) => r.status === 200 || r.status === 503,
  });
  
  sleep(1);
}
```

**Expected Results**:
- At 100 users: p95 < 500ms ✅
- At 200 users: p95 < 1000ms ⚠️
- At 300 users: p95 > 2000ms, errors start ❌
- At 400 users: 50%+ error rate ❌

**Conclusion**: System breaks at ~250 concurrent users.

---

## Spike Test (Sudden Traffic)

**tests/performance/spike-test.js**:

```javascript
export const options = {
  stages: [
    { duration: '1m', target: 10 },    // Normal load
    { duration: '1m', target: 1000 },  // SPIKE!
    { duration: '3m', target: 1000 },  // Sustain spike
    { duration: '1m', target: 10 },    // Back to normal
    { duration: '1m', target: 0 },     // Shutdown
  ],
  thresholds: {
    http_req_duration: ['p(95)<1000'],  // More lenient during spike
    http_req_failed: ['rate<0.05'],     // 5% error tolerance
  },
};
```

**Goal**: Verify system recovers after spike.

---

## Soak Test (Memory Leaks)

**tests/performance/soak-test.js**:

```javascript
export const options = {
  stages: [
    { duration: '5m', target: 100 },    // Ramp up
    { duration: '2h', target: 100 },    // Sustain load for 2 hours
    { duration: '5m', target: 0 },      // Ramp down
  ],
};
```

**What to monitor**:
- Memory usage (should be stable)
- Response time (should not degrade over time)
- Error rate (should stay low)

**Signs of memory leak**:
- Increasing memory usage over time
- Degrading response times after 1+ hours
- Eventual crash

---

## Authentication Flow Test

**tests/performance/auth-test.js**:

```javascript
import http from 'k6/http';
import { check } from 'k6';

export const options = {
  stages: [
    { duration: '1m', target: 50 },
  ],
};

const BASE_URL = __ENV.BASE_URL || 'https://api.eshop.com';

export default function () {
  // Login
  const loginRes = http.post(`${BASE_URL}/api/v1/auth/login`, JSON.stringify({
    email: `testuser${__VU}@example.com`,  // Virtual User ID
    password: 'Test123!',
  }), {
    headers: { 'Content-Type': 'application/json' },
  });

  check(loginRes, {
    'login successful': (r) => r.status === 200,
    'access token received': (r) => JSON.parse(r.body).accessToken !== undefined,
  });

  const accessToken = JSON.parse(loginRes.body).accessToken;

  // Authenticated request
  const productsRes = http.get(`${BASE_URL}/api/v1/products`, {
    headers: { Authorization: `Bearer ${accessToken}` },
  });

  check(productsRes, {
    'products fetched': (r) => r.status === 200,
  });
}
```

---

## Checkout Flow Test

**tests/performance/checkout-test.js**:

```javascript
export default function () {
  const accessToken = login();

  // Add to basket
  const productId = getRandomProductId();
  const addToBasketRes = http.post(`${BASE_URL}/api/v1/basket/items`, JSON.stringify({
    productId: productId,
    quantity: 1,
  }), {
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`,
    },
  });

  check(addToBasketRes, { 'added to basket': (r) => r.status === 200 });

  sleep(2);

  // Checkout
  const checkoutRes = http.post(`${BASE_URL}/api/v1/basket/checkout`, JSON.stringify({
    address: '123 Test St',
    city: 'Test City',
    zipCode: '12345',
  }), {
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${accessToken}`,
    },
  });

  check(checkoutRes, { 'checkout successful': (r) => r.status === 200 });
}

function login() {
  const res = http.post(`${BASE_URL}/api/v1/auth/login`, JSON.stringify({
    email: `user${__VU}@example.com`,
    password: 'Test123!',
  }), {
    headers: { 'Content-Type': 'application/json' },
  });

  return JSON.parse(res.body).accessToken;
}

function getRandomProductId() {
  const res = http.get(`${BASE_URL}/api/v1/products?page=1&pageSize=10`);
  const products = JSON.parse(res.body).items;
  return products[Math.floor(Math.random() * products.length)].id;
}
```

---

## Database Query Performance

**tests/performance/db-query-test.js**:

```javascript
import http from 'k6/http';
import { check } from 'k6';

export const options = {
  stages: [
    { duration: '1m', target: 100 },
  ],
  thresholds: {
    'http_req_duration{endpoint:search}': ['p(95)<200'],      // Search should be fast
    'http_req_duration{endpoint:details}': ['p(95)<100'],     // Simple GET
    'http_req_duration{endpoint:complex}': ['p(95)<500'],     // Complex query
  },
};

export default function () {
  // Simple query
  let res = http.get(`${BASE_URL}/api/v1/products/123`, {
    tags: { endpoint: 'details' },
  });
  check(res, { 'details < 100ms': (r) => r.timings.duration < 100 });

  // Complex query (with joins, filters)
  res = http.get(`${BASE_URL}/api/v1/products?category=electronics&minPrice=100&maxPrice=500&sort=price`, {
    tags: { endpoint: 'search' },
  });
  check(res, { 'search < 200ms': (r) => r.timings.duration < 200 });

  // Very complex query
  res = http.get(`${BASE_URL}/api/v1/reports/sales?from=2024-01-01&to=2024-12-31&groupBy=category`, {
    tags: { endpoint: 'complex' },
  });
  check(res, { 'complex < 500ms': (r) => r.timings.duration < 500 });
}
```

---

## Results Analysis

### K6 Output

```
     ✓ status is 200
     ✓ response time < 500ms

     checks.........................: 100.00% ✓ 29890      ✗ 0    
     data_received..................: 45 MB   150 kB/s
     data_sent......................: 2.1 MB  7.0 kB/s
     http_req_blocked...............: avg=1.2ms    min=0s    med=0s    max=234ms p(90)=0s    p(95)=0s   
     http_req_connecting............: avg=412µs    min=0s    med=0s    max=89ms  p(90)=0s    p(95)=0s   
     http_req_duration..............: avg=156ms    min=12ms  med=98ms  max=1.2s  p(90)=312ms p(95)=456ms
       { expected_response:true }...: avg=156ms    min=12ms  med=98ms  max=1.2s  p(90)=312ms p(95)=456ms
     http_req_failed................: 0.00%   ✓ 0          ✗ 14945
     http_req_receiving.............: avg=234µs    min=0s    med=0s    max=45ms  p(90)=1ms   p(95)=2ms  
     http_req_sending...............: avg=45µs     min=0s    med=0s    max=12ms  p(90)=0s    p(95)=0s   
     http_req_tls_handshaking.......: avg=678µs    min=0s    med=0s    max=145ms p(90)=0s    p(95)=0s   
     http_req_waiting...............: avg=155ms    min=12ms  med=98ms  max=1.2s  p(90)=311ms p(95)=455ms
     http_reqs......................: 14945   49.816799/s
     iteration_duration.............: avg=3.15s    min=3s    med=3.1s  max=4.2s  p(90)=3.3s  p(95)=3.5s 
     iterations.....................: 4982    16.605599/s
     vus............................: 100     min=100      max=100
     vus_max........................: 100     min=100      max=100

running (5m00.0s), 000/100 VUs, 4982 complete and 0 interrupted iterations
```

**Key Metrics**:
- **http_req_duration p(95)**: 456ms ✅ (< 500ms threshold)
- **http_req_failed**: 0% ✅ (no errors)
- **http_reqs**: 49.8 req/s
- **vus**: 100 concurrent users

---

## Grafana + InfluxDB Integration

### Setup InfluxDB

```bash
docker run -d -p 8086:8086 \
  -v influxdb:/var/lib/influxdb \
  influxdb:1.8
```

### Run K6 with InfluxDB

```bash
k6 run --out influxdb=http://localhost:8086/k6 tests/performance/load-test.js
```

### Grafana Dashboard

Import K6 dashboard: https://grafana.com/grafana/dashboards/2587

---

## Performance Benchmarks

| Endpoint | p50 | p95 | p99 | Threshold |
|----------|-----|-----|-----|-----------|
| **GET /products** | 50ms | 150ms | 300ms | ✅ < 200ms |
| **GET /products/:id** | 20ms | 80ms | 150ms | ✅ < 100ms |
| **POST /basket/checkout** | 150ms | 400ms | 800ms | ✅ < 500ms |
| **POST /auth/login** | 100ms | 250ms | 500ms | ✅ < 300ms |

---

## Load Testing Best Practices

✅ **Do**:
- Test on staging (similar to production)
- Gradually ramp up load
- Monitor system resources (CPU, memory, database)
- Use realistic scenarios (not just hitting one endpoint)
- Run during off-peak hours

❌ **Don't**:
- Test production directly (use staging)
- Start with max load immediately
- Ignore warnings (check logs)
- Run tests from laptop (use cloud: Azure Load Testing, K6 Cloud)

---

## CI/CD Integration

**GitHub Actions**:

```yaml
name: Performance Tests

on:
  schedule:
    - cron: '0 2 * * 0'  # Weekly, Sunday 2 AM

jobs:
  performance:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Run K6 load test
        uses: grafana/k6-action@v0.3.0
        with:
          filename: tests/performance/load-test.js
          flags: --out influxdb=http://influxdb:8086/k6
        env:
          BASE_URL: https://staging-api.eshop.com
      
      - name: Upload results
        uses: actions/upload-artifact@v3
        with:
          name: k6-results
          path: summary.json
```

---

## When Performance Degrades

### Investigation Steps

1. **Check Grafana dashboards**: CPU, memory, disk I/O
2. **Check database queries**: Slow query log
3. **Check APM traces** (Application Performance Monitoring)
4. **Check logs**: Errors, timeouts
5. **Check network**: Latency between services

### Common Issues

| Symptom | Likely Cause | Solution |
|---------|--------------|----------|
| Slow p95, fast p50 | Database slow queries | Add indexes |
| High memory | Memory leak | Profile with dotMemory |
| High CPU | Inefficient algorithm | Profile with dotTrace |
| Timeouts | External API slow | Add timeout, retry, circuit breaker |
| 503 errors | Service overloaded | Scale horizontally |

---

**Версія**: 1.0  
**Останнє оновлення**: 2024-01-15
