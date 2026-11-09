export const environment = {
  production: false,
  /**
   * Relative '/api' keeps same-origin calls working both in dev (via
   * proxy.conf.cjs → http://localhost:5000) and with the managed Azure
   * Functions API deployed by Azure Static Web Apps.
   */
  apiUrl: '/api',
};
