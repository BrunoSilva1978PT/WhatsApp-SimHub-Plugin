/**
 * WhatsApp SimHub Plugin - Baileys Backend
 * @version 1.0.0
 */
const SCRIPT_VERSION = "1.0.0";

import makeWASocket from "@whiskeysockets/baileys";
import {
  DisconnectReason,
  useMultiFileAuthState,
  fetchLatestBaileysVersion,
  makeCacheableSignalKeyStore,
} from "@whiskeysockets/baileys";
import pino from "pino";
import { WebSocket, WebSocketServer } from "ws";
import fs from "fs";
import path from "path";
import os from "os";

const appData =
  process.env.APPDATA || path.join(os.homedir(), "AppData", "Roaming");
const dataPath = path.join(appData, "SimHub", "WhatsAppPlugin", "data_baileys");
const authPath = path.join(dataPath, "auth_info");
const logPath = path.join(
  appData,
  "SimHub",
  "WhatsAppPlugin",
  "logs",
  "baileys.log",
);

// Create directories
try {
  const logDir = path.dirname(logPath);
  if (!fs.existsSync(logDir)) fs.mkdirSync(logDir, { recursive: true });
  if (!fs.existsSync(dataPath)) fs.mkdirSync(dataPath, { recursive: true });
  fs.writeFileSync(logPath, "");
} catch (e) {}

function log(msg) {
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
    log("[STACK] " + err.stack);
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

log("[BAILEYS] Starting Baileys server...");
log("[WS] Starting server on port 3000...");

const wss = new WebSocketServer({
  port: 3000,
  host: "127.0.0.1",
});

let ws = null;
let sock = null;
let isConnected = false;
let seenMessages = new Set();
let readyTimestamp = 0;

wss.on("error", (error) => {
  log("[WS] Server error: " + error.message);
});

wss.on("listening", () => {
  log("[WS] Server successfully listening on 127.0.0.1:3000");
});

function send(data) {
  if (ws && ws.readyState === WebSocket.OPEN) {
    try {
      ws.send(JSON.stringify(data));
      log("[WS] Sent: " + data.type);
    } catch (err) {
      log("[WS] Send error: " + err.message);
    }
  } else {
    log("[WS] Cannot send - no connection");
  }
}

wss.on("connection", (socket) => {
  log("[WS] Plugin connected!");
  ws = socket;

  socket.on("message", async (msg) => {
    try {
      const data = JSON.parse(msg);
      log("[WS] Received: " + data.type);

      if (data.type === "connect") {
        if (!sock || !isConnected) {
          log("[BAILEYS] Connecting to WhatsApp...");
          await connectToWhatsApp();
        } else {
          log("[BAILEYS] Already connected");
          send({ type: "already_connected" });
        }
      }
      if (data.type === "sendReply") {
        try {
          log(`[REPLY] Sending to: ${data.chatId}`);
          log(`[REPLY] Text: ${data.text}`);

          const jid = data.chatId;
          await sock.sendMessage(jid, { text: data.text });

          log("[REPLY] Message sent successfully!");
        } catch (error) {
          log(`[REPLY] FAILED: ${error.message}`);
        }
      } else if (data.type === "refreshChatContacts") {
        log("[CHATS] Chat contacts refresh NOT SUPPORTED in Baileys");
        send({
          type: "chatContactsError",
          error:
            "Chat contacts list is not supported with Baileys backend. Please use whatsapp-web.js.",
        });
      }
    } catch (err) {
      log("[WS] Message error: " + err.message);
    }
  });

  socket.on("error", (error) => {
    log("[WS] Socket error: " + error.message);
  });

  socket.on("close", () => {
    log("[WS] Plugin disconnected");
    ws = null;
  });
});

async function connectToWhatsApp() {
  try {
    const { state, saveCreds } = await useMultiFileAuthState(authPath);

    const { version } = await fetchLatestBaileysVersion();
    log("[BAILEYS] WhatsApp Web version: " + version.join("."));

    sock = makeWASocket({
      version,
      auth: {
        creds: state.creds,
        keys: makeCacheableSignalKeyStore(
          state.keys,
          pino({ level: "silent" }),
        ),
      },
      printQRInTerminal: false,
      getMessage: async () => undefined,
    });

    sock.ev.on("connection.update", async (update) => {
      const { connection, lastDisconnect, qr } = update;

      if (qr) {
        log("[QR] QR Code generated");
        send({ type: "qr", qr: qr });
      }

      if (connection === "close") {
        isConnected = false;
        const shouldReconnect =
          lastDisconnect?.error?.output?.statusCode !==
          DisconnectReason.loggedOut;
        log(
          "[BAILEYS] Connection closed. Reason: " +
            (lastDisconnect?.error?.message || "Unknown"),
        );

        if (shouldReconnect) {
          log("[BAILEYS] Reconnecting...");
          setTimeout(() => connectToWhatsApp(), 5000);
        } else {
          log("[BAILEYS] Logged out");
          send({ type: "disconnected", reason: "Logged out" });
        }
      } else if (connection === "open") {
        isConnected = true;
        readyTimestamp = Date.now();

        log("[BAILEYS] Connected successfully!");

        const me = sock.user;
        const number = me?.id?.split(":")[0] || "Unknown";
        const name = me?.name || "User";

        log("[BAILEYS] Number: " + number + ", Name: " + name);
        send({ type: "ready", number: number, name: name });
      }
    });

    sock.ev.on("creds.update", saveCreds);

    sock.ev.on("messages.upsert", async (m) => {
      if (!isConnected) return;
      if (m.type !== "notify") return;

      const msg = m.messages[0];

      if (!msg.message || msg.key.remoteJid === "status@broadcast") return;
      if (msg.key.fromMe) return;
      if (seenMessages.has(msg.key.id)) return;

      if (readyTimestamp > 0 && msg.messageTimestamp * 1000 < readyTimestamp) {
        return;
      }

      seenMessages.add(msg.key.id);

      const sender = msg.key.remoteJid;
      const isGroup = sender.endsWith("@g.us");
      const senderName = msg.pushName || "Unknown";

      let number = null;

      if (msg.key.senderPn) {
        number = msg.key.senderPn.replace("@s.whatsapp.net", "");
        log("[MSG] Got number from senderPn: " + number);
      } else if (sender.includes("@s.whatsapp.net")) {
        number = sender.replace("@s.whatsapp.net", "");
        log("[MSG] Got number from sender (direct): " + number);
      } else if (sender.includes("@lid")) {
        log("[MSG] WARNING: LinkedID detected, no real number available");
        log("[MSG] Baileys cannot resolve LID to real number reliably");
        number = sender.split("@")[0];
      }

      if (!number) {
        log("[MSG] REJECTED - Could not get number");
        return;
      }

      log("[MSG] NEW from: " + senderName + " (" + number + ")");

      let messageContent = "";
      let messageType = "";
      let hasMedia = false;

      if (msg.message.conversation) {
        messageContent = msg.message.conversation;
        messageType = "text";
      } else if (msg.message.extendedTextMessage) {
        messageContent = msg.message.extendedTextMessage.text;
        messageType = "text";
      } else if (msg.message.imageMessage) {
        messageContent = msg.message.imageMessage.caption || "[Image]";
        messageType = "image";
        hasMedia = true;
      } else if (msg.message.videoMessage) {
        messageContent = msg.message.videoMessage.caption || "[Video]";
        messageType = "video";
        hasMedia = true;
      } else if (msg.message.audioMessage) {
        messageContent = "[Audio]";
        messageType = "audio";
        hasMedia = true;
      } else if (msg.message.documentMessage) {
        messageContent = msg.message.documentMessage.fileName || "[Document]";
        messageType = "document";
        hasMedia = true;
      } else if (msg.message.stickerMessage) {
        messageContent = "[Sticker]";
        messageType = "sticker";
        hasMedia = true;
      } else {
        log("[MSG] Unknown message type, skipping");
        return;
      }

      log("[MSG] Type: " + messageType + ", Content: " + messageContent);

      const data = {
        id: msg.key.id,
        from: senderName,
        number: number,
        body: messageContent,
        timestamp: msg.messageTimestamp * 1000,
        chatId: sender,
        hasMedia: hasMedia,
      };

      if (hasMedia) {
        data.mediaType = messageType;
      }

      log("[MSG] Sending to C#: from=" + senderName + ", number=" + number);
      send({ type: "message", message: data });
    });
  } catch (error) {
    log("[BAILEYS] Error: " + error.message);
    log("[BAILEYS] Stack: " + error.stack);
  }
}

log("[BAILEYS] Server ready - waiting for plugin connection...");
