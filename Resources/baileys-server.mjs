/**
 * WhatsApp SimHub Plugin - Baileys Backend
 * Based on official Baileys v7 example pattern
 */
const SCRIPT_VERSION = "1.0.0";

import makeWASocket, {
  DisconnectReason,
  useMultiFileAuthState,
  makeCacheableSignalKeyStore,
  fetchLatestBaileysVersion,
} from "@whiskeysockets/baileys";
import { Boom } from "@hapi/boom";
import pino from "pino";
import { WebSocket, WebSocketServer } from "ws";
import fs from "fs";
import path from "path";
import os from "os";
import { execSync } from "child_process";

// ============================================================================
// PATHS & SETUP
// ============================================================================

const appData =
  process.env.APPDATA || path.join(os.homedir(), "AppData", "Roaming");
const pluginDir = path.join(appData, "SimHub", "WhatsAppPlugin");
const authPath = path.join(pluginDir, "data_baileys", "auth_info");
const logPath = path.join(pluginDir, "logs", "baileys.log");

// Create directories
try {
  fs.mkdirSync(path.dirname(logPath), { recursive: true });
  fs.mkdirSync(path.dirname(authPath), { recursive: true });
} catch (e) {}

// ============================================================================
// LOGGING
// ============================================================================

function log(msg) {
  const line = `[${new Date().toISOString().substring(11, 23)}] ${msg}`;
  try {
    console.log(line);
  } catch (e) {
    if (e.code !== "EPIPE") throw e;
  }
  try {
    fs.appendFileSync(logPath, line + "\n");
  } catch (e) {}
}

// Pino logger for Baileys (silent)
const logger = pino({ level: "silent" });

// ============================================================================
// GLOBAL STATE
// ============================================================================

let pluginSocket = null; // WebSocket connection to C# plugin
let whatsappSocket = null; // Baileys WhatsApp socket
let isConnecting = false; // Prevent multiple simultaneous connections
let seenMessages = new Set();
let readyTimestamp = 0;

// LID to phone number cache (v7 uses LIDs instead of phone numbers)
const lidToPhoneCache = new Map();

// ============================================================================
// ERROR HANDLING
// ============================================================================

process.on("uncaughtException", (err) => {
  if (err.code === "EPIPE") return;
  log(`[FATAL] Uncaught exception: ${err.message}`);
});

process.on("unhandledRejection", (err) => {
  log(`[FATAL] Unhandled rejection: ${err}`);
});

// ============================================================================
// PORT SELECTION
// ============================================================================

function findAvailablePort(startPort = 3000, endPort = 3100) {
  try {
    const netstatOutput = execSync("netstat -ano", { encoding: "utf8" });
    const usedPorts = new Set();

    for (const line of netstatOutput.split("\n")) {
      const match = line.match(/(?:127\.0\.0\.1|0\.0\.0\.0):(\d+)/);
      if (match) usedPorts.add(parseInt(match[1], 10));
    }

    for (let port = startPort; port <= endPort; port++) {
      if (!usedPorts.has(port)) return port;
    }
  } catch (e) {}
  return startPort;
}

const selectedPort = findAvailablePort(3000, 3100);
console.log("PORT:" + selectedPort); // C# reads this

log(`[BAILEYS] Starting server v${SCRIPT_VERSION}`);

// ============================================================================
// WEBSOCKET SERVER (Communication with C# Plugin)
// ============================================================================

const wss = new WebSocketServer({ port: selectedPort, host: "127.0.0.1" });

wss.on("error", (error) => log(`[WS] Server error: ${error.message}`));
wss.on("listening", () => log(`[WS] Listening on 127.0.0.1:${selectedPort}`));

/**
 * Send data to C# plugin
 */
function sendToPlugin(data) {
  if (pluginSocket && pluginSocket.readyState === WebSocket.OPEN) {
    try {
      pluginSocket.send(JSON.stringify(data));
      log(`[WS] Sent: ${data.type}`);
    } catch (err) {
      log(`[WS] Send error: ${err.message}`);
    }
  }
}

/**
 * Handle WebSocket connection from C# plugin
 */
wss.on("connection", (socket) => {
  log("[WS] Plugin connected");
  pluginSocket = socket;

  socket.on("message", async (msg) => {
    try {
      const data = JSON.parse(msg);
      log(`[WS] Received: ${data.type}`);

      switch (data.type) {
        case "connect":
          startWhatsApp();
          break;

        case "shutdown":
          log("[BAILEYS] Shutdown requested");
          if (whatsappSocket) {
            try {
              whatsappSocket.end();
            } catch (e) {}
            whatsappSocket = null;
          }
          process.exit(0);
          break;

        case "sendReply":
          await handleSendReply(data);
          break;

        case "refreshChatContacts":
          sendToPlugin({
            type: "chatContactsError",
            error: "Chat contacts not supported with Baileys.",
          });
          break;
      }
    } catch (err) {
      log(`[WS] Message error: ${err.message}`);
    }
  });

  socket.on("close", () => {
    log("[WS] Plugin disconnected");
    pluginSocket = null;
  });
});

// ============================================================================
// WHATSAPP MESSAGE HANDLING
// ============================================================================

/**
 * Handle sending a reply
 */
async function handleSendReply(data) {
  if (!whatsappSocket) {
    log("[REPLY] Not connected");
    return;
  }
  try {
    await whatsappSocket.sendMessage(data.chatId, { text: data.text });
    log(`[REPLY] Sent to ${data.chatId}`);
  } catch (error) {
    log(`[REPLY] Failed: ${error.message}`);
  }
}

/**
 * Resolve LID to phone number using v7 methods
 */
async function resolveToPhoneNumber(lid, msgKey = {}) {
  if (!lid.includes("@lid")) {
    return lid.split("@")[0];
  }

  if (lidToPhoneCache.has(lid)) {
    return lidToPhoneCache.get(lid);
  }

  if (msgKey.remoteJidAlt?.includes("@s.whatsapp.net")) {
    const phone = msgKey.remoteJidAlt.replace("@s.whatsapp.net", "");
    lidToPhoneCache.set(lid, phone);
    log(`[LID] Resolved via remoteJidAlt: ${lid} -> ${phone}`);
    return phone;
  }

  if (msgKey.participantAlt?.includes("@s.whatsapp.net")) {
    const phone = msgKey.participantAlt.replace("@s.whatsapp.net", "");
    lidToPhoneCache.set(lid, phone);
    log(`[LID] Resolved via participantAlt: ${lid} -> ${phone}`);
    return phone;
  }

  if (whatsappSocket?.signalRepository?.lidMapping?.getPNForLID) {
    try {
      const pn =
        await whatsappSocket.signalRepository.lidMapping.getPNForLID(lid);
      if (pn) {
        const phone = pn.replace("@s.whatsapp.net", "");
        lidToPhoneCache.set(lid, phone);
        log(`[LID] Resolved via lidMapping: ${lid} -> ${phone}`);
        return phone;
      }
    } catch (e) {}
  }

  return lid.split("@")[0];
}

// ============================================================================
// WHATSAPP CONNECTION
// ============================================================================

/**
 * Start WhatsApp connection
 */
async function startWhatsApp() {
  // Prevent multiple simultaneous connection attempts
  if (isConnecting) {
    log("[BAILEYS] Already connecting, skipping...");
    return;
  }

  isConnecting = true;

  try {
    // Close previous socket if exists
    if (whatsappSocket) {
      log("[BAILEYS] Closing previous socket...");
      try {
        whatsappSocket.end();
      } catch (e) {}
      whatsappSocket = null;
      // Wait a bit for cleanup
      await new Promise((resolve) => setTimeout(resolve, 1000));
    }

    log("[BAILEYS] Starting WhatsApp connection...");

    // Get auth state
    const { state, saveCreds } = await useMultiFileAuthState(authPath);

    // Get latest WhatsApp Web version
    let version;
    try {
      const versionInfo = await fetchLatestBaileysVersion();
      version = versionInfo.version;
      log(`[BAILEYS] Using WA version: ${version.join(".")}`);
    } catch (e) {
      log(`[BAILEYS] Could not fetch version, using default`);
      version = undefined;
    }

    // Create socket
    whatsappSocket = makeWASocket({
      version,
      logger,
      auth: {
        creds: state.creds,
        keys: makeCacheableSignalKeyStore(state.keys, logger),
      },
      printQRInTerminal: false,
      getMessage: async () => undefined,
    });

    // Process events
    whatsappSocket.ev.process(async (events) => {
      // CONNECTION UPDATE
      if (events["connection.update"]) {
        const update = events["connection.update"];
        const { connection, lastDisconnect, qr } = update;

        if (qr) {
          log("[QR] Generated - waiting for scan");
          sendToPlugin({ type: "qr", qr: qr });
        }

        if (connection === "close") {
          const statusCode = lastDisconnect?.error?.output?.statusCode;
          const shouldReconnect = statusCode !== DisconnectReason.loggedOut;

          log(
            `[BAILEYS] Connection closed. Code: ${statusCode}, Reconnecting: ${shouldReconnect}`,
          );

          // Reset connecting flag
          isConnecting = false;

          if (shouldReconnect) {
            // Wait before reconnecting to avoid rapid reconnection loops
            setTimeout(() => startWhatsApp(), 3000);
          } else {
            log("[BAILEYS] Logged out - clearing session");
            sendToPlugin({ type: "disconnected", reason: "Logged out" });
            try {
              fs.rmSync(authPath, { recursive: true, force: true });
            } catch (e) {}
          }
        }

        if (connection === "open") {
          isConnecting = false;
          readyTimestamp = Date.now();
          seenMessages.clear();

          const me = whatsappSocket.user;
          const number = me?.id?.split(":")[0] || "Unknown";
          const name = me?.name || "User";

          log(`[BAILEYS] Connected: ${number} (${name})`);
          sendToPlugin({ type: "ready", number: number, name: name });
        }
      }

      // CREDENTIALS UPDATE
      if (events["creds.update"]) {
        await saveCreds();
      }

      // LID MAPPING UPDATE
      if (events["lid-mapping.update"]) {
        const mapping = events["lid-mapping.update"];
        if (mapping?.lid && mapping?.pn) {
          const phone = mapping.pn.replace("@s.whatsapp.net", "");
          lidToPhoneCache.set(mapping.lid, phone);
        }
      }

      // MESSAGING HISTORY
      if (events["messaging-history.set"]) {
        const { contacts } = events["messaging-history.set"];
        if (contacts) {
          for (const contact of contacts) {
            if (contact.id?.includes("@lid") && contact.phoneNumber) {
              lidToPhoneCache.set(
                contact.id,
                contact.phoneNumber.replace("@s.whatsapp.net", ""),
              );
            } else if (contact.lid && contact.id?.includes("@s.whatsapp.net")) {
              lidToPhoneCache.set(
                contact.lid,
                contact.id.replace("@s.whatsapp.net", ""),
              );
            }
          }
        }
      }

      // CONTACTS UPDATE
      if (events["contacts.update"]) {
        for (const contact of events["contacts.update"]) {
          if (contact.id?.includes("@lid") && contact.phoneNumber) {
            lidToPhoneCache.set(
              contact.id,
              contact.phoneNumber.replace("@s.whatsapp.net", ""),
            );
          }
        }
      }

      // MESSAGES
      if (events["messages.upsert"]) {
        const upsert = events["messages.upsert"];
        if (upsert.type !== "notify") return;

        for (const msg of upsert.messages) {
          if (!msg.message) continue;
          if (msg.key.remoteJid === "status@broadcast") continue;
          if (msg.key.fromMe) continue;
          if (seenMessages.has(msg.key.id)) continue;
          if (
            readyTimestamp > 0 &&
            msg.messageTimestamp * 1000 < readyTimestamp
          )
            continue;

          seenMessages.add(msg.key.id);

          const chatJid = msg.key.remoteJid;
          const senderName = msg.pushName || "Unknown";
          let phoneNumber = null;
          let isLid = false;

          if (msg.key.participant) {
            const participant = msg.key.participant;
            if (participant.includes("@lid")) {
              isLid = true;
              phoneNumber = await resolveToPhoneNumber(participant, msg.key);
            } else {
              phoneNumber = participant.replace("@s.whatsapp.net", "");
            }
          } else if (msg.key.senderPn) {
            phoneNumber = msg.key.senderPn.replace("@s.whatsapp.net", "");
          } else if (chatJid.includes("@s.whatsapp.net")) {
            phoneNumber = chatJid.replace("@s.whatsapp.net", "");
          } else if (chatJid.includes("@lid")) {
            isLid = true;
            phoneNumber = await resolveToPhoneNumber(chatJid, msg.key);
          }

          if (!phoneNumber) continue;

          const m = msg.message;
          let messageContent = "";
          let messageType = "";
          let hasMedia = false;

          if (m.conversation) {
            messageContent = m.conversation;
            messageType = "text";
          } else if (m.extendedTextMessage) {
            messageContent = m.extendedTextMessage.text;
            messageType = "text";
          } else if (m.imageMessage) {
            messageContent = m.imageMessage.caption || "[Image]";
            messageType = "image";
            hasMedia = true;
          } else if (m.videoMessage) {
            messageContent = m.videoMessage.caption || "[Video]";
            messageType = "video";
            hasMedia = true;
          } else if (m.audioMessage) {
            messageContent = "[Audio]";
            messageType = "audio";
            hasMedia = true;
          } else if (m.documentMessage) {
            messageContent = m.documentMessage.fileName || "[Document]";
            messageType = "document";
            hasMedia = true;
          } else if (m.stickerMessage) {
            messageContent = "[Sticker]";
            messageType = "sticker";
            hasMedia = true;
          } else {
            continue;
          }

          log(`[MSG] From: ${senderName} (${phoneNumber})`);

          const messageData = {
            id: msg.key.id,
            from: senderName,
            number: phoneNumber,
            body: messageContent,
            timestamp: msg.messageTimestamp * 1000,
            chatId: chatJid,
            hasMedia: hasMedia,
            isLid: isLid,
          };

          if (hasMedia) {
            messageData.mediaType = messageType;
          }

          sendToPlugin({ type: "message", message: messageData });
        }
      }
    });
  } catch (error) {
    log(`[BAILEYS] Error starting connection: ${error.message}`);
    isConnecting = false;
    // Retry after delay
    setTimeout(() => startWhatsApp(), 5000);
  }
}

// ============================================================================
// STARTUP
// ============================================================================

log("[BAILEYS] Server ready - waiting for plugin connection...");
