export const environment = {
  production: false,
  /**
   * Development values. Mirrors environment.ts; kept for parity. The build
   * intentionally does not use fileReplacements — environment.ts already
   * works in every configuration because apiUrl is a relative path.
   */
  apiUrl: '/api',
  auth0: {
    domain: 'dev-fwr0a0nxb1dt4f46.us.auth0.com',
    clientId: 'IDv6Vf6vsZnh5A0VzGevrfX8YuC1uL3M',
    audience: 'https://api.hockeyseason.dev',
  },
};

