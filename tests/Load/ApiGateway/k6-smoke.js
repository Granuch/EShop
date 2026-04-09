import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  vus: 5,
  duration: '30s',
  thresholds: {
    http_req_failed: ['rate<0.20'],
    http_req_duration: ['p(95)<1200']
  }
};

const baseUrl = __ENV.GATEWAY_BASE_URL || 'http://localhost:7000';

export default function () {
  const health = http.get(`${baseUrl}/health/live`);
  check(health, {
    'health/live status is 200': (r) => r.status === 200
  });

  const sim = http.get(`${baseUrl}/api/v1/orders`, {
    headers: {
      'X-Simulate': 'true'
    }
  });

  check(sim, {
    'simulated orders returns success': (r) => r.status >= 200 && r.status < 300
  });

  sleep(1);
}
