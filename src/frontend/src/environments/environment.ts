export const environment = {
  production: false,
  /**
   * Relative '/api' keeps same-origin calls working both in dev (via
   * proxy.conf.json → http://localhost:5000) and when served behind a
   * managed API integration (e.g. Azure Static Web Apps).
   */
  apiUrl: '/api',
};
