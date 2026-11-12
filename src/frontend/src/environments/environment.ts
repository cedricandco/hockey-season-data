export const environment = {
  production: false,
  /**
   * Relative '/api' keeps same-origin calls working both in dev (via
   * proxy.conf.cjs → http://localhost:7071) and with the managed Azure
   * Functions API deployed by Azure Static Web Apps.
   */
  apiUrl: '/api',
  /**
   * Auth0 SPA application (public client): domain and client id are not
   * secrets — the tokens are validated against the backend's OIDC authority.
   * The audience must match the Auth0 "API" identifier used by the backend
   * (Auth:Audience in appsettings.json).
   */
  auth0: {
    domain: 'dev-fwr0a0nxb1dt4f46.us.auth0.com',
    clientId: 'IDv6Vf6vsZnh5A0VzGevrfX8YuC1uL3M',
    audience: 'https://api.hockeyseason.dev',
  },
};

