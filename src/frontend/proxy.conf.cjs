module.exports = {
  '/api': {
    target: process.env.API_BACKEND_URL || 'http://localhost:5000',
    secure: false,
    changeOrigin: true,
  },
};
