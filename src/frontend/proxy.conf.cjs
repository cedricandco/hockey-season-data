module.exports = {
  '/api': {
    target: process.env.API_BACKEND_URL || 'http://localhost:7071',
    secure: false,
    changeOrigin: true,
  },
};
