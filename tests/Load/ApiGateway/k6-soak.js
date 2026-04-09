import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  scenarios: {
    steady_gateway_load: {
      executor: 'ramping-vus',
      startVUs: 5,
      stages: [
        { duration: '5m', target: 25 },
        { duration: '20m', target: 25 },
        { duration: '5m', target: 5 }
      ]
    }
  },
  thresholds: {
    http_req_failed: ['rate<0.25'],
    http_req_duration: ['p(95)<1500'],
    checks: ['rate>0.95']
  }
};

const baseUrl = __ENV.GATEWAY_BASE_URL || 'http://localhost:7000';

export default function () {
  const health = http.get(`${baseUrl}/health/ready`);
  check(health, {
    'health/ready returns 200': (r) => r.status === 200
  });

  const simulated = http.get(`${baseUrl}/api/v1/orders`, {
    headers: {
      'X-Simulate': 'true'
    }
  });

  check(simulated, {
    'simulated route responds': (r) => r.status >= 200 && r.status < 600
  });

  const passthrough = http.get(`${baseUrl}/api/v1/orders`, {
    headers: {
      'X-Simulate': 'false'
    }
  });

  check(passthrough, {
    'passthrough returns known status': (r) => [200, 401, 429, 502].includes(r.status)
  });

  sleep(1);
}
