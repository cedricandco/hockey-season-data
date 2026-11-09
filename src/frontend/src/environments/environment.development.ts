export const environment = {
  production: false,
  /**
   * Development values. Mirrors environment.ts; kept for parity. The build
   * intentionally does not use fileReplacements — environment.ts already
   * works in every configuration because apiUrl is a relative path.
   */
  apiUrl: '/api',
};
