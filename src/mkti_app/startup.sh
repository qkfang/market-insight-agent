#!/usr/bin/env bash
# Install the native shared libraries that headless Chromium (PuppeteerSharp)
# needs to launch on the Debian-based Azure App Service Linux image.
# The blessed .NET image filesystem (outside /home) is reset on every cold
# start, so these packages must be (re)installed each time the container boots.
set -u

echo "[startup] Installing Chromium native dependencies for PuppeteerSharp..."
apt-get update -y \
  && apt-get install -y --no-install-recommends \
    libnss3 \
    libnspr4 \
    libatk1.0-0 \
    libatk-bridge2.0-0 \
    libcups2 \
    libdrm2 \
    libxkbcommon0 \
    libxcomposite1 \
    libxdamage1 \
    libxfixes3 \
    libxrandr2 \
    libgbm1 \
    libpango-1.0-0 \
    libcairo2 \
    libasound2 \
    libatspi2.0-0 \
    libgtk-3-0 \
    libx11-6 \
    libxcb1 \
    libxext6 \
    libxi6 \
    libxtst6 \
    libxshmfence1 \
    fonts-liberation \
  || echo "[startup] WARNING: apt-get failed; PDF generation may not work."

echo "[startup] Starting application..."
exec dotnet mkti_app.dll
