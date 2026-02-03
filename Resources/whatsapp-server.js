/**
 * WhatsApp SimHub Plugin - WhatsApp Web.js Backend
 * @version 1.0.0
 */
const SCRIPT_VERSION = "1.0.0";

const { Client, LocalAuth } = require("whatsapp-web.js");
const WebSocket = require("ws");
const fs = require("fs");
const path = require("path");
const os = require("os");
const { execSync } = require("child_process");

const appData =
  process.env.APPDATA || path.join(os.homedir(), "AppData", "Roaming");
const pluginDir = path.join(appData, "SimHub", "WhatsAppPlugin");
const dataPath = path.join(pluginDir, "data");
const logPath = path.join(pluginDir, "logs", "node.log");
const debugConfigPath = path.join(pluginDir, "config", "debug.json");

// Google Contacts module
const googleContacts = require("./google-contacts.js");
googleContacts.init(pluginDir);

try {
  const logDir = path.dirname(logPath);
  if (!fs.existsSync(logDir)) fs.mkdirSync(logDir, { recursive: true });
  fs.writeFileSync(logPath, "");
} catch (e) {}

function isDebugEnabled() {
  try {
    if (fs.existsSync(debugConfigPath)) {
      const config = JSON.parse(fs.readFileSync(debugConfigPath, "utf8"));
      return config.enabled === true;
    }
  } catch (e) {}
  return false;
}

function log(msg) {
  // Only log if debug is enabled
  if (!isDebugEnabled()) return;

  const line = "[" + new Date().toISOString().substring(11, 23) + "] " + msg;
  try {
    console.log(line);
  } catch (e) {
    if (e.code !== "EPIPE") throw e;
  }
  try {
    fs.appendFileSync(logPath, line + "\n");
  } catch (e) {}
}

let isHandlingError = false;

process.on("uncaughtException", (err) => {
  if (err.code === "EPIPE") return;
  if (isHandlingError) return;
  isHandlingError = true;
  try {
    log("[FATAL] Uncaught exception: " + err.message);
  } catch (e) {}
  isHandlingError = false;
});

process.on("unhandledRejection", (err) => {
  if (isHandlingError) return;
  isHandlingError = true;
  try {
    log("[FATAL] Unhandled rejection: " + err);
  } catch (e) {}
  isHandlingError = false;
});

function findAvailablePort(startPort = 3000, endPort = 3100) {
  try {
    const netstatOutput = execSync("netstat -ano", { encoding: "utf8" });
    const lines = netstatOutput.split("\n");
    const usedPorts = new Set();

    for (const line of lines) {
      const match = line.match(/(?:127\.0\.0\.1|0\.0\.0\.0):(\d+)/);
      if (match) {
        usedPorts.add(parseInt(match[1], 10));
      }
    }

    for (let port = startPort; port <= endPort; port++) {
      if (!usedPorts.has(port)) {
        return port;
      }
    }
    return startPort;
  } catch (error) {
    return startPort;
  }
}

const selectedPort = findAvailablePort(3000, 3100);
console.log("PORT:" + selectedPort);

log("[WS] Script version: " + SCRIPT_VERSION);

const wss = new WebSocket.Server({
  port: selectedPort,
  host: "127.0.0.1",
});

let ws = null;
let pendingReadyData = null;
let pendingContactsList = null;

wss.on("error", (error) => {
  log("[WS] Server error: " + error.message);
});

wss.on("listening", () => {
  log("[WS] Server listening on 127.0.0.1:" + selectedPort);
});

function send(data) {
  if (ws && ws.readyState === WebSocket.OPEN) {
    try {
      ws.send(JSON.stringify(data));
      log("[WS] Sent: " + data.type);
      return true;
    } catch (err) {
      return false;
    }
  }
  return false;
}

wss.on("connection", (socket) => {
  log("[WS] Plugin connected!");
  ws = socket;

  if (isReady && pendingReadyData) {
    send(pendingReadyData);
    if (pendingContactsList) {
      send(pendingContactsList);
    }
  }

  socket.on("message", async (msg) => {
    try {
      const data = JSON.parse(msg);
      log("[WS] Received: " + data.type);

      if (data.type === "shutdown") {
        log("[WA] Shutdown initiated...");
        client.destroy().then(() => {
          process.exit(0);
        });
      } else if (data.type === "sendReply") {
        try {
          const result = await client.pupPage.evaluate(
            async (chatId, messageText) => {
              try {
                const chat = await window.Store.Chat.find(chatId);
                if (!chat) {
                  return { success: false, error: "Chat not found" };
                }
                const message = await window.WWebJS.sendMessage(
                  chat,
                  messageText,
                  {},
                  0,
                );
                return { success: true, messageId: message.id._serialized };
              } catch (err) {
                return { success: false, error: err.toString() };
              }
            },
            data.chatId,
            data.text,
          );

          if (!result.success) {
            throw new Error(result.error);
          }
          log("[REPLY] Sent to " + data.chatId);
        } catch (error) {
          log("[REPLY] Failed: " + error.message);
        }
      } else if (data.type === "googleGetStatus") {
        // Get Google Contacts connection status
        const status = googleContacts.getStatus();
        send({ type: "googleStatus", ...status });
      } else if (data.type === "googleStartAuth") {
        // Check if already connected with valid tokens
        if (googleContacts.isConnected()) {
          log("[GOOGLE] Already connected - using existing session");
          send({ type: "googleAuthComplete", success: true });
          return;
        }

        // Start Google OAuth flow
        log("[GOOGLE] Starting OAuth flow...");
        const authUrl = googleContacts.getAuthUrlForBrowser();
        send({ type: "googleAuthUrl", url: authUrl });

        // Start auth server and wait for callback
        googleContacts
          .startAuth()
          .then((result) => {
            log("[GOOGLE] OAuth completed successfully");
            send({ type: "googleAuthComplete", success: true });
          })
          .catch((err) => {
            log("[GOOGLE] OAuth failed: " + err.message);
            send({
              type: "googleAuthComplete",
              success: false,
              error: err.message,
            });
          });
      } else if (data.type === "googleGetContacts") {
        // Fetch contacts from Google API and save to file
        log("[GOOGLE] Refreshing contacts...");
        const forceRefresh = data.forceRefresh || false;
        googleContacts
          .getContacts(forceRefresh)
          .then(() => {
            // Signal done - plugin reads file directly
            send({ type: "googleContactsDone" });
          })
          .catch((err) => {
            if (err.message === "AUTH_REQUIRED") {
              send({
                type: "googleStatus",
                connected: false,
                status: "AUTH_REQUIRED",
              });
            } else {
              send({ type: "googleError", error: err.message });
            }
          });
      } else if (data.type === "googleDisconnect") {
        // Disconnect Google account
        log("[GOOGLE] Disconnecting...");
        googleContacts.disconnect();
        send({
          type: "googleStatus",
          connected: false,
          status: "NOT_CONNECTED",
        });
      } else if (data.type === "refreshChatContacts") {
        log("[CHATS] Refreshing contacts...");
        client
          .getChats()
          .then(async (chats) => {
            const validContacts = [];
            for (const chat of chats) {
              if (chat.isGroup) continue;
              const id = chat.id?.user || chat.id?._serialized;
              if (id) {
                // Try to get contact info for better name
                let name = chat.name;
                try {
                  const contact = await chat.getContact();
                  if (contact) {
                    // Priority: pushname > name > number
                    name =
                      contact.pushname ||
                      contact.name ||
                      contact.number ||
                      chat.name;
                  }
                } catch (e) {
                  // Keep chat.name if contact fetch fails
                }
                validContacts.push({ name: name || "(No name)", number: id });
              }
            }
            validContacts.sort((a, b) => a.name.localeCompare(b.name));
            pendingContactsList = {
              type: "chatContactsList",
              contacts: validContacts,
            };
            send(pendingContactsList);
          })
          .catch((error) => {
            send({ type: "chatContactsError", error: error.message });
          });
      } else if (data.type === "checkWhatsApp") {
        // Check if a phone number has WhatsApp
        const phoneNumber = data.number;
        if (!phoneNumber) {
          send({
            type: "checkWhatsAppResult",
            exists: false,
            error: "No number provided",
          });
          return;
        }

        if (!isReady) {
          send({
            type: "checkWhatsAppResult",
            exists: false,
            error: "Not connected to WhatsApp",
          });
          return;
        }

        // Format number (remove + and spaces, add @c.us)
        const cleanNumber = phoneNumber.replace(/[\s\+\-]/g, "");
        const numberId = cleanNumber + "@c.us";

        log("[CHECK] Checking if " + cleanNumber + " has WhatsApp...");
        client
          .isRegisteredUser(numberId)
          .then((exists) => {
            log("[CHECK] " + cleanNumber + " has WhatsApp: " + exists);
            send({
              type: "checkWhatsAppResult",
              number: phoneNumber,
              exists: exists,
            });
          })
          .catch((error) => {
            log("[CHECK] Error: " + error.message);
            send({
              type: "checkWhatsAppResult",
              exists: false,
              error: error.message,
            });
          });
      }
    } catch (err) {
      log("[WS] Error: " + err.message);
    }
  });

  socket.on("close", () => {
    log("[WS] Plugin disconnected");
    ws = null;
  });
});

log("[WA] Data path: " + dataPath);
if (!fs.existsSync(dataPath)) {
  fs.mkdirSync(dataPath, { recursive: true });
}

const client = new Client({
  authStrategy: new LocalAuth({ clientId: "simhub", dataPath: dataPath }),
  puppeteer: {
    headless: true,
    args: ["--no-sandbox", "--disable-setuid-sandbox"],
    userAgent:
      "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
  },
  authTimeoutMs: 60000,
  webVersionCache: {
    type: "remote",
    remotePath:
      "https://raw.githubusercontent.com/wppconnect-team/wa-version/main/html/2.2412.54.html",
  },
});

let seenMessages = new Set();
let isReady = false;
let pollingInterval = null;
let eventsWorking = false;
let eventsCheckTimeout = null;
let readyTimestamp = 0;

async function processMessage(msg) {
  try {
    if (seenMessages.has(msg.id._serialized)) return;
    if (msg.fromMe) return;
    if (readyTimestamp > 0 && msg.timestamp * 1000 < readyTimestamp) return;

    seenMessages.add(msg.id._serialized);

    let number = null;
    let name = msg.notifyName || msg.author || "Unknown";

    // Direct number from @c.us
    if (msg.from.includes("@c.us")) {
      number = msg.from.split("@")[0];
    }
    // LinkedID - try to get real number
    else if (msg.from.includes("@lid")) {
      // Method 1: getContactById
      try {
        const contact = await client.getContactById(msg.from);
        if (contact && contact.number) {
          number = contact.number;
          if (contact.pushname) name = contact.pushname;
          else if (contact.name) name = contact.name;
        }
      } catch (err) {}

      // Method 2: getChats
      if (!number) {
        try {
          const chats = await client.getChats();
          const chat = chats.find((c) => c.id._serialized === msg.from);
          if (chat && chat.contact) {
            if (chat.contact.number) number = chat.contact.number;
            if (chat.contact.pushname) name = chat.contact.pushname;
            else if (chat.contact.name) name = chat.contact.name;
          }
        } catch (err) {}
      }

      // Method 3: Store access
      if (!number) {
        try {
          const storeNumber = await client.pupPage.evaluate((chatId) => {
            try {
              const Store = window.Store;
              if (Store && Store.Contact) {
                const contact = Store.Contact.get(chatId);
                if (contact && contact.id && contact.id.user) {
                  return contact.id.user;
                }
              }
            } catch (e) {}
            return null;
          }, msg.from);
          if (storeNumber) number = storeNumber;
        } catch (err) {}
      }

      if (!number) {
        log("[MSG] Could not resolve LID: " + msg.from);
        return;
      }
    } else {
      return;
    }

    log("[MSG] From: " + name + " (" + number + ")");

    const data = {
      id: msg.id._serialized,
      from: name,
      number: number,
      body: msg.body || "",
      timestamp: msg.timestamp * 1000,
      chatId: msg.from,
      hasMedia: msg.hasMedia,
    };

    if (msg.hasMedia) {
      try {
        const media = await msg.downloadMedia();
        let type = "unknown";
        if (msg.type === "image") type = "image";
        else if (msg.type === "video") type = "video";
        else if (msg.type === "audio" || msg.type === "ptt") type = "audio";
        else if (msg.type === "sticker") type = "sticker";
        else if (msg.type === "document") type = "document";
        data.mediaType = type;
        data.mediaData = media.data;
        data.mediaMimetype = media.mimetype;
      } catch (err) {}
    }

    send({ type: "message", message: data });
  } catch (err) {
    log("[MSG] Error: " + err.message);
  }
}

function startPolling() {
  if (pollingInterval) return;

  log("[POLL] Starting polling mode...");
  pollingInterval = setInterval(async () => {
    if (!isReady) return;
    try {
      const chats = await client.getChats();
      for (const chat of chats) {
        if (chat.isGroup) continue;
        try {
          const messages = await chat.fetchMessages({ limit: 5 });
          for (const msg of messages) {
            await processMessage(msg);
          }
        } catch (err) {}
      }
    } catch (err) {}
  }, 5000);
}

client.on("loading_screen", (percent, message) => {
  log("[LOADING] " + percent + "% - " + message);
});

client.on("qr", (qr) => {
  log("[QR] Generated");
  send({ type: "qr", qr: qr });
});

client.on("authenticated", () => {
  log("[AUTH] Authenticated");
});

client.on("auth_failure", (msg) => {
  log("[AUTH] Failed: " + msg);
});

client.on("message_create", async (msg) => {
  eventsWorking = true;
  if (eventsCheckTimeout) {
    clearTimeout(eventsCheckTimeout);
    eventsCheckTimeout = null;
  }
  await processMessage(msg);
});

client.on("ready", async () => {
  log("[READY] Client ready!");

  // LID migration fix
  try {
    await client.pupPage.evaluate(() => {
      window.Store.Cmd.default.isLidMigrated = () => true;
    });
  } catch (err) {}

  readyTimestamp = Date.now();

  if (client.info && client.info.wid) {
    const number = client.info.wid.user;
    const name = client.info.pushname || "User";
    log("[READY] Number: " + number + ", Name: " + name);
    pendingReadyData = { type: "ready", number: number, name: name };
    send(pendingReadyData);
  }

  isReady = true;

  // Get chat contacts
  client
    .getChats()
    .then(async (chats) => {
      const validContacts = [];
      for (const chat of chats) {
        if (chat.isGroup) continue;
        const id = chat.id?.user || chat.id?._serialized;
        if (id) {
          // Try to get contact info for better name
          let name = chat.name;
          try {
            const contact = await chat.getContact();
            if (contact) {
              // Priority: pushname > name > number
              name =
                contact.pushname || contact.name || contact.number || chat.name;
            }
          } catch (e) {
            // Keep chat.name if contact fetch fails
          }
          validContacts.push({ name: name || "(No name)", number: id });
        }
      }
      validContacts.sort((a, b) => a.name.localeCompare(b.name));
      log("[CHATS] " + validContacts.length + " contacts loaded");
      pendingContactsList = {
        type: "chatContactsList",
        contacts: validContacts,
      };
      send(pendingContactsList);
    })
    .catch((error) => {
      send({ type: "chatContactsError", error: error.message });
    });

  // Fallback to polling if events don't work
  eventsCheckTimeout = setTimeout(() => {
    if (!eventsWorking) {
      startPolling();
    }
  }, 30000);
});

client.on("disconnected", (reason) => {
  log("[WA] Disconnected: " + reason);
  isReady = false;
  pendingReadyData = null;
  pendingContactsList = null;
  send({ type: "disconnected", reason: reason });
});

log("[WA] Initializing...");
client.initialize();
