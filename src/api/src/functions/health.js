const { app } = require('@azure/functions');

// Mirrors GET /api/health of the .NET API so the Static Web App can serve it.
app.http('health', {
  methods: ['GET'],
  authLevel: 'anonymous',
  route: 'health',
  handler: async () => ({
    jsonBody: {
      environment: process.env.AZURE_FUNCTIONS_ENVIRONMENT || 'Production',
      time: { utcNow: new Date().toISOString(), timeZoneId: 'UTC' },
    },
  }),
});
