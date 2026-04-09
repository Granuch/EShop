# API Gateway Load/Soak Tests

## Prerequisites
- Docker profile running with API Gateway on `http://localhost:7000`
- [k6](https://k6.io/docs/get-started/installation/) installed

## Smoke test (quick)
```bash
k6 run tests/Load/ApiGateway/k6-smoke.js
```

## Soak test (30 minutes)
```bash
k6 run tests/Load/ApiGateway/k6-soak.js
```

## Custom endpoint
```bash
k6 run -e GATEWAY_BASE_URL=http://localhost:7000 tests/Load/ApiGateway/k6-soak.js
```

## Notes
- Tests intentionally allow known gateway outcomes in passthrough mode (`401/429/502`) to reflect real auth/rate-limit/downstream states.
- For production-like execution, point `GATEWAY_BASE_URL` to ingress endpoint and run from CI runner with stable network.
