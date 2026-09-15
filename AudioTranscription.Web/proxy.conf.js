const apiUrl = process.env["services__api__https__0"] || process.env["services__api__http__0"] || "https://localhost:5001";

/** @type {import('@angular/build').ServeOptions['proxyConfig']} */
const proxyConfig = [
  {
    context: ["/api", "/hubs"],
    target: apiUrl,
    secure: false,
    changeOrigin: true,
    ws: true,
  },
];

module.exports = proxyConfig;
