/**
 * Google Contacts Module for WhatsApp SimHub Plugin
 * @version 1.0.0
 *
 * Handles OAuth2 authentication and fetching contacts from Google People API
 */

const SCRIPT_VERSION = "1.0.0";

const https = require("https");
const http = require("http");
const fs = require("fs");
const path = require("path");
const url = require("url");

// Google OAuth2 credentials
const CLIENT_ID =
  "50909009643-204fjdcgf0t089um4e3c8cl5n5lc7f8a.apps.googleusercontent.com";
const CLIENT_SECRET = "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";
const REDIRECT_URI = "http://localhost:8956/oauth2callback";
const SCOPES = ["https://www.googleapis.com/auth/contacts.readonly"];

// File paths (will be set based on data directory)
let dataDir = "";
let tokensFile = "";
let contactsCacheFile = "";
let logFile = "";

/**
 * Log message to dedicated Google Contacts log file
 */
function log(message) {
  const timestamp = new Date().toISOString();
  const logMessage = `[${timestamp}] ${message}\n`;
  console.log(`[GOOGLE] ${message}`);

  try {
    if (logFile) {
      fs.appendFileSync(logFile, logMessage);
    }
  } catch (err) {
    // Ignore log write errors
  }
}

// State
let tokens = null;
let authServer = null;
let authResolve = null;
let authReject = null;

/**
 * Initialize the module with the plugin directory
 * Creates data_google subfolder for tokens and cache
 */
function init(pluginDirectory) {
  // Use data_google subfolder to keep files organized
  dataDir = path.join(pluginDirectory, "data_google");
  tokensFile = path.join(dataDir, "tokens.json");
  contactsCacheFile = path.join(dataDir, "contacts.json");

  // Set up log file in logs directory
  const logsDir = path.join(pluginDirectory, "logs");
  if (!fs.existsSync(logsDir)) {
    fs.mkdirSync(logsDir, { recursive: true });
  }
  logFile = path.join(logsDir, "google-contacts.log");

  // Clear log file on init
  try {
    fs.writeFileSync(
      logFile,
      `=== Google Contacts Log Started ${new Date().toISOString()} ===\n`,
    );
  } catch (err) {
    // Ignore
  }

  // Ensure data directory exists
  if (!fs.existsSync(dataDir)) {
    fs.mkdirSync(dataDir, { recursive: true });
  }

  log(`Initialized with dataDir: ${dataDir}`);
  log(`Tokens file: ${tokensFile}`);
  log(`Cache file: ${contactsCacheFile}`);

  // Load existing tokens and update status
  const tokensLoaded = loadTokens();
  if (tokensLoaded) {
    log("Existing Google session found - user is connected");
  }
}

/**
 * Load tokens from file
 */
function loadTokens() {
  try {
    if (fs.existsSync(tokensFile)) {
      const data = fs.readFileSync(tokensFile, "utf8");
      tokens = JSON.parse(data);
      log("Tokens loaded from file");
      return true;
    } else {
      log("No tokens file found");
    }
  } catch (err) {
    log(`Error loading tokens: ${err.message}`);
  }
  tokens = null;
  return false;
}

/**
 * Save tokens to file
 */
function saveTokens() {
  try {
    fs.writeFileSync(tokensFile, JSON.stringify(tokens, null, 2));
    log("Tokens saved to file");
  } catch (err) {
    log(`Error saving tokens: ${err.message}`);
  }
}

/**
 * Check if connected (has valid tokens)
 */
function isConnected() {
  return tokens !== null && tokens.access_token;
}

/**
 * Get the OAuth2 authorization URL
 */
function getAuthUrl() {
  const params = new URLSearchParams({
    client_id: CLIENT_ID,
    redirect_uri: REDIRECT_URI,
    response_type: "code",
    scope: SCOPES.join(" "),
    access_type: "offline",
    prompt: "consent",
  });

  return `https://accounts.google.com/o/oauth2/auth?${params.toString()}`;
}

// Auth timeout (2 minutes)
const AUTH_TIMEOUT_MS = 120000;
let authTimeout = null;

/**
 * Start OAuth2 flow - starts local server to receive callback
 * Returns the auth URL immediately, server waits for callback
 */
function startAuth() {
  return new Promise((resolve, reject) => {
    // Clear any existing timeout
    if (authTimeout) {
      clearTimeout(authTimeout);
      authTimeout = null;
    }

    // Close existing server if any
    if (authServer) {
      try {
        authServer.close();
      } catch (e) {}
      authServer = null;
    }

    authResolve = resolve;
    authReject = reject;

    // Set timeout for auth flow (2 minutes)
    authTimeout = setTimeout(() => {
      log("Auth timeout - closing server");
      if (authServer) {
        try {
          authServer.close();
        } catch (e) {}
        authServer = null;
      }
      authReject(new Error("Authentication timeout. Please try again."));
    }, AUTH_TIMEOUT_MS);

    // Start local server to receive OAuth callback
    authServer = http.createServer(async (req, res) => {
      const parsedUrl = url.parse(req.url, true);

      if (parsedUrl.pathname === "/oauth2callback") {
        // Clear timeout on callback
        if (authTimeout) {
          clearTimeout(authTimeout);
          authTimeout = null;
        }

        const code = parsedUrl.query.code;
        const error = parsedUrl.query.error;

        if (error) {
          res.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
          res.end(
            '<html><body style="font-family:sans-serif;text-align:center;padding:50px"><h1>Authorization Failed</h1><p>You can close this window.</p></body></html>',
          );
          authServer.close();
          authServer = null;
          authReject(new Error(error));
          return;
        }

        if (code) {
          try {
            // Exchange code for tokens
            await exchangeCodeForTokens(code);

            res.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
            res.end(
              '<html><body style="font-family:sans-serif;text-align:center;padding:50px"><h1 style="color:green">Authorization Successful!</h1><p>You can close this window and return to SimHub.</p></body></html>',
            );

            authServer.close();
            authServer = null;
            authResolve({ success: true });
          } catch (err) {
            res.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
            res.end(
              `<html><body style="font-family:sans-serif;text-align:center;padding:50px"><h1 style="color:red">Authorization Failed</h1><p>${err.message}</p></body></html>`,
            );

            authServer.close();
            authServer = null;
            authReject(err);
          }
        }
      } else {
        // Handle other requests (e.g., favicon)
        res.writeHead(404);
        res.end();
      }
    });

    authServer.listen(8956, "127.0.0.1", () => {
      log("OAuth callback server started on 127.0.0.1:8956");
    });

    authServer.on("error", (err) => {
      log(`Server error: ${err.message}`);
      if (authTimeout) {
        clearTimeout(authTimeout);
        authTimeout = null;
      }
      authReject(err);
    });
  });
}

/**
 * Get the auth URL (for external use)
 */
function getAuthUrlForBrowser() {
  return getAuthUrl();
}

/**
 * Exchange authorization code for tokens
 */
function exchangeCodeForTokens(code) {
  return new Promise((resolve, reject) => {
    const postData = new URLSearchParams({
      client_id: CLIENT_ID,
      client_secret: CLIENT_SECRET,
      code: code,
      grant_type: "authorization_code",
      redirect_uri: REDIRECT_URI,
    }).toString();

    const options = {
      hostname: "oauth2.googleapis.com",
      path: "/token",
      method: "POST",
      headers: {
        "Content-Type": "application/x-www-form-urlencoded",
        "Content-Length": Buffer.byteLength(postData),
      },
    };

    const req = https.request(options, (res) => {
      let data = "";
      res.on("data", (chunk) => (data += chunk));
      res.on("end", () => {
        try {
          const response = JSON.parse(data);
          if (response.error) {
            reject(new Error(response.error_description || response.error));
          } else {
            tokens = response;
            tokens.obtained_at = Date.now();
            saveTokens();
            log("Tokens obtained successfully");
            resolve(tokens);
          }
        } catch (err) {
          reject(err);
        }
      });
    });

    req.on("error", reject);
    req.write(postData);
    req.end();
  });
}

/**
 * Refresh access token using refresh token
 */
function refreshAccessToken() {
  return new Promise((resolve, reject) => {
    if (!tokens || !tokens.refresh_token) {
      reject(new Error("No refresh token available"));
      return;
    }

    const postData = new URLSearchParams({
      client_id: CLIENT_ID,
      client_secret: CLIENT_SECRET,
      refresh_token: tokens.refresh_token,
      grant_type: "refresh_token",
    }).toString();

    const options = {
      hostname: "oauth2.googleapis.com",
      path: "/token",
      method: "POST",
      headers: {
        "Content-Type": "application/x-www-form-urlencoded",
        "Content-Length": Buffer.byteLength(postData),
      },
    };

    const req = https.request(options, (res) => {
      let data = "";
      res.on("data", (chunk) => (data += chunk));
      res.on("end", () => {
        try {
          const response = JSON.parse(data);
          if (response.error) {
            // Refresh token invalid - need to re-authenticate
            tokens = null;
            saveTokens();
            reject(new Error("AUTH_REQUIRED"));
          } else {
            // Keep the refresh token (Google doesn't always return it)
            tokens.access_token = response.access_token;
            tokens.expires_in = response.expires_in;
            tokens.obtained_at = Date.now();
            saveTokens();
            log("Access token refreshed");
            resolve(tokens);
          }
        } catch (err) {
          reject(err);
        }
      });
    });

    req.on("error", reject);
    req.write(postData);
    req.end();
  });
}

/**
 * Check if access token is expired
 */
function isTokenExpired() {
  if (!tokens || !tokens.obtained_at || !tokens.expires_in) {
    return true;
  }

  const expiresAt = tokens.obtained_at + tokens.expires_in * 1000;
  // Consider expired 5 minutes before actual expiry
  return Date.now() > expiresAt - 5 * 60 * 1000;
}

/**
 * Ensure we have a valid access token
 */
async function ensureValidToken() {
  if (!tokens) {
    throw new Error("AUTH_REQUIRED");
  }

  if (isTokenExpired()) {
    await refreshAccessToken();
  }

  return tokens.access_token;
}

/**
 * Fetch a single page of contacts from Google People API
 */
function fetchContactsPage(accessToken, pageToken = null) {
  return new Promise((resolve, reject) => {
    let path =
      "/v1/people/me/connections?personFields=names,phoneNumbers&pageSize=1000";
    if (pageToken) {
      path += `&pageToken=${encodeURIComponent(pageToken)}`;
    }

    const options = {
      hostname: "people.googleapis.com",
      path: path,
      method: "GET",
      headers: {
        Authorization: `Bearer ${accessToken}`,
      },
    };

    const req = https.request(options, (res) => {
      let data = "";
      res.on("data", (chunk) => (data += chunk));
      res.on("end", () => {
        try {
          const response = JSON.parse(data);
          if (response.error) {
            reject(response.error);
          } else {
            resolve(response);
          }
        } catch (err) {
          reject(err);
        }
      });
    });

    req.on("error", reject);
    req.end();
  });
}

/**
 * Detect the most common country prefix from a list of phone numbers
 */
function detectCountryPrefix(phoneNumbers) {
  const prefixCount = {};

  for (const phone of phoneNumbers) {
    if (phone.startsWith("+")) {
      // Extract prefix (first 2-4 digits after +)
      // Common prefixes: +1 (US/CA), +44 (UK), +351 (PT), +55 (BR), etc.
      const match = phone.match(/^\+(\d{1,4})/);
      if (match) {
        const prefix = "+" + match[1];
        prefixCount[prefix] = (prefixCount[prefix] || 0) + 1;
      }
    }
  }

  // Find the most common prefix
  let mostCommon = null;
  let maxCount = 0;
  for (const [prefix, count] of Object.entries(prefixCount)) {
    if (count > maxCount) {
      maxCount = count;
      mostCommon = prefix;
    }
  }

  return mostCommon;
}

/**
 * Fetch all contacts from Google People API (with pagination support)
 */
async function fetchContacts() {
  log("fetchContacts starting...");
  const accessToken = await ensureValidToken();
  log("Got valid access token");

  const rawContacts = []; // Contacts before prefix normalization
  let pageToken = null;
  let pageCount = 0;

  try {
    // First pass: collect all contacts
    do {
      pageCount++;
      log(
        `Fetching page ${pageCount}${pageToken ? " (with pageToken)" : ""}...`,
      );

      const response = await fetchContactsPage(accessToken, pageToken);

      if (response.error) {
        if (response.error.code === 401) {
          log("Token expired, refreshing...");
          await refreshAccessToken();
          return fetchContacts(); // Retry with new token
        }
        throw new Error(response.error.message);
      }

      const connectionsCount = response.connections?.length || 0;
      log(`Page ${pageCount}: ${connectionsCount} connections`);

      if (response.connections) {
        for (const person of response.connections) {
          const name = person.names?.[0]?.displayName;
          const phones = person.phoneNumbers || [];

          for (const phone of phones) {
            if (name && phone.value) {
              // Normalize phone number (remove spaces, dashes, parentheses)
              let phoneNumber = phone.value.replace(/[\s\-\(\)]/g, "");
              rawContacts.push({ name, phoneNumber });
            }
          }
        }
      }

      pageToken = response.nextPageToken;
    } while (pageToken);

    // Detect country prefix from numbers that have it
    const numbersWithPrefix = rawContacts
      .map((c) => c.phoneNumber)
      .filter((n) => n.startsWith("+"));
    const defaultPrefix = detectCountryPrefix(numbersWithPrefix);
    log(`Detected default country prefix: ${defaultPrefix || "none"}`);

    // Second pass: normalize all contacts
    const allContacts = [];
    for (const { name, phoneNumber } of rawContacts) {
      let normalizedNumber = phoneNumber;

      if (!phoneNumber.startsWith("+")) {
        if (defaultPrefix) {
          // Add detected country prefix to numbers without one
          // Remove leading 0 if present (common in local numbers in many countries)
          const localNumber = phoneNumber.replace(/^0+/, "");
          normalizedNumber = defaultPrefix + localNumber;
          log(`Normalized ${phoneNumber} -> ${normalizedNumber}`);
        } else {
          // No prefix detected and number has no prefix - skip it
          continue;
        }
      }

      allContacts.push({
        name: name,
        number: normalizedNumber,
        displayText: `${name} (${normalizedNumber})`,
      });
    }

    // Sort contacts alphabetically by name
    allContacts.sort((a, b) => a.name.localeCompare(b.name));

    log(
      `Fetched ${allContacts.length} contacts from Google API (${pageCount} pages)`,
    );

    // Save to cache
    saveContactsCache(allContacts);

    return allContacts;
  } catch (err) {
    log(`Error fetching contacts: ${err.message}`);
    throw err;
  }
}

/**
 * Save contacts to cache file
 */
function saveContactsCache(contacts) {
  try {
    const cache = {
      lastUpdated: new Date().toISOString(),
      contacts: contacts,
    };
    fs.writeFileSync(contactsCacheFile, JSON.stringify(cache, null, 2));
    log("Contacts cache saved");
  } catch (err) {
    log(`Error saving contacts cache: ${err.message}`);
  }
}

/**
 * Load contacts from cache file
 */
function loadContactsCache() {
  try {
    if (fs.existsSync(contactsCacheFile)) {
      const data = fs.readFileSync(contactsCacheFile, "utf8");
      const cache = JSON.parse(data);
      log(`Loaded ${cache.contacts?.length || 0} contacts from cache`);
      return cache;
    }
  } catch (err) {
    log(`Error loading contacts cache: ${err.message}`);
  }
  return null;
}

/**
 * Get contacts (from cache or fetch)
 */
async function getContacts(forceRefresh = false) {
  log(`getContacts called with forceRefresh=${forceRefresh}`);

  if (!forceRefresh) {
    const cache = loadContactsCache();
    if (cache && cache.contacts) {
      log(`Returning ${cache.contacts.length} contacts from cache`);
      return cache;
    }
  }

  // Fetch from Google
  log("Fetching contacts from Google API...");
  const contacts = await fetchContacts();
  log(`Returning ${contacts.length} contacts from API`);
  return {
    lastUpdated: new Date().toISOString(),
    contacts: contacts,
  };
}

/**
 * Disconnect from Google (UI only - keeps tokens for reconnect)
 * Tokens are only removed if forceLogout is true
 */
function disconnect(forceLogout = false) {
  log(`Disconnecting... (forceLogout=${forceLogout})`);

  if (forceLogout) {
    // Full logout - remove tokens file
    tokens = null;
    try {
      if (fs.existsSync(tokensFile)) {
        fs.unlinkSync(tokensFile);
        log("Tokens file removed (full logout)");
      }
    } catch (err) {
      log(`Error removing tokens file: ${err.message}`);
    }
  } else {
    // Soft disconnect - keep tokens in memory for quick reconnect
    log("Soft disconnect - tokens kept for reconnect");
  }

  log("Disconnected");
}

/**
 * Get connection status
 */
function getStatus() {
  if (!tokens) {
    return { connected: false, status: "NOT_CONNECTED" };
  }

  const cache = loadContactsCache();
  return {
    connected: true,
    status: "CONNECTED",
    lastSync: cache?.lastUpdated || null,
  };
}

module.exports = {
  init,
  isConnected,
  startAuth,
  getAuthUrlForBrowser,
  getContacts,
  fetchContacts,
  disconnect,
  getStatus,
  loadContactsCache,
};
