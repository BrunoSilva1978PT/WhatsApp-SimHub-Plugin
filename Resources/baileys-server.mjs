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
  jidNormalizedUser,
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

// Cache de LID → número real (populado quando recebe contatos)
const lidToNumberCache = new Map();

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

        // Tentar popular cache de LID → número real
        try {
          log("[LID-CACHE] Attempting to build LID to phone number mapping...");

          // Pegar chats para tentar mapear LIDs
          const chats = await sock
            .groupFetchAllParticipating()
            .catch(() => ({}));

          // Também tentar pegar de store se existir
          if (sock.store?.contacts) {
            log(
              "[LID-CACHE] Found " +
                Object.keys(sock.store.contacts).length +
                " contacts in store",
            );

            for (const [jid, contact] of Object.entries(sock.store.contacts)) {
              if (jid.includes("@lid") && contact.notify) {
                // Tentar extrair número real
                const possibleNumber = contact.notify || contact.name;
                if (possibleNumber && /^\d+$/.test(possibleNumber)) {
                  lidToNumberCache.set(jid, possibleNumber);
                  log("[LID-CACHE] Mapped: " + jid + " → " + possibleNumber);
                }
              }
            }
          }

          log(
            "[LID-CACHE] Built cache with " +
              lidToNumberCache.size +
              " mappings",
          );
        } catch (e) {
          log("[LID-CACHE] Warning: Could not build LID cache: " + e.message);
        }

        send({ type: "ready", number: number, name: name });
      }
    });

    sock.ev.on("creds.update", saveCreds);

    // NOVO: Listener para atualizações automáticas de LID mapping
    sock.ev.on("lid-mapping.update", (mapping) => {
      if (mapping && mapping.lid && mapping.pn) {
        const lid = mapping.lid;
        const pn = mapping.pn.replace("@s.whatsapp.net", "");
        lidToNumberCache.set(lid, pn);
        log("[LID-MAPPING] Auto-stored: " + lid + " → " + pn);
      }
    });

    // Listener para capturar info de contatos e popular cache LID (ATUALIZADO v7)
    sock.ev.on("messaging-history.set", ({ contacts }) => {
      if (!contacts) return;
      log("[LID-CACHE] Received contacts from history: " + contacts.length);

      for (const contact of contacts) {
        // v7 structure: id + (lid OR phoneNumber)
        if (contact.id && contact.id.includes("@lid") && contact.phoneNumber) {
          const lid = contact.id;
          const pn = contact.phoneNumber.replace("@s.whatsapp.net", "");
          lidToNumberCache.set(lid, pn);
          log("[LID-CACHE] Mapped from history: " + lid + " → " + pn);
        } else if (
          contact.lid &&
          contact.id &&
          contact.id.includes("@s.whatsapp.net")
        ) {
          // Alternative case: id is PN, lid field exists
          const lid = contact.lid;
          const pn = contact.id.replace("@s.whatsapp.net", "");
          lidToNumberCache.set(lid, pn);
          log("[LID-CACHE] Mapped from history (alt): " + lid + " → " + pn);
        }
      }
    });

    // Listener para grupos - participantes têm LID + PN pairings
    sock.ev.on("groups.upsert", async (groups) => {
      for (const group of groups) {
        if (!group.participants) continue;

        for (const participant of group.participants) {
          // Participantes seguem estrutura Contact (id + lid/phoneNumber)
          if (
            participant.id &&
            participant.id.includes("@lid") &&
            participant.phoneNumber
          ) {
            const lid = participant.id;
            const pn = participant.phoneNumber.replace("@s.whatsapp.net", "");
            lidToNumberCache.set(lid, pn);
            log("[LID-CACHE] Mapped from group: " + lid + " → " + pn);
          }
        }
      }
    });

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
      let isLid = false;

      // Tentar obter número real
      if (msg.key.participant) {
        // Mensagem de grupo
        const participant = msg.key.participant;

        // NOVO v7: Verificar participantAlt primeiro (tem PN se participant é LID)
        if (msg.key.participantAlt) {
          const alt = msg.key.participantAlt;
          if (alt.includes("@s.whatsapp.net")) {
            number = alt.replace("@s.whatsapp.net", "");
            log("[MSG] ✅ Got number from participantAlt: " + number);
            // Se participant é LID, guardar mapeamento
            if (participant.includes("@lid")) {
              isLid = true;
              lidToNumberCache.set(participant, number);
              log("[MSG] Stored LID mapping: " + participant + " → " + number);
            }
          }
        }

        // Se não tem participantAlt, processar participant normal
        if (!number) {
          if (participant.includes("@lid")) {
            isLid = true;
            log("[MSG] WARNING: LinkedID in participant, no participantAlt");
            number = participant.split("@")[0]; // fallback
          } else {
            number = participant.replace("@s.whatsapp.net", "");
            log("[MSG] Got number from participant: " + number);
          }
        }
      } else if (msg.key.senderPn) {
        number = msg.key.senderPn.replace("@s.whatsapp.net", "");
        log("[MSG] Got number from senderPn: " + number);
      } else if (sender.includes("@s.whatsapp.net")) {
        number = sender.replace("@s.whatsapp.net", "");
        log("[MSG] Got number from sender (direct): " + number);
      } else if (sender.includes("@lid")) {
        isLid = true;
        log("[MSG] WARNING: LinkedID detected in sender: " + sender);

        // NOVO v7: Verificar remoteJidAlt primeiro (tem PN se remoteJid é LID)
        if (
          msg.key.remoteJidAlt &&
          msg.key.remoteJidAlt.includes("@s.whatsapp.net")
        ) {
          number = msg.key.remoteJidAlt.replace("@s.whatsapp.net", "");
          log("[MSG] ✅ Got number from remoteJidAlt: " + number);
          // Guardar mapeamento
          lidToNumberCache.set(sender, number);
        }
        // PRIORIDADE 2: Usar signalRepository.lidMapping
        else {
          try {
            const pn =
              await sock.signalRepository.lidMapping.getPNForLID(sender);
            if (pn) {
              number = pn.replace("@s.whatsapp.net", "");
              log("[MSG] ✅ Got PN from signalRepository: " + number);
              lidToNumberCache.set(sender, number);
            } else {
              throw new Error("No PN in signalRepository");
            }
          } catch (e) {
            log("[MSG] signalRepository lookup failed: " + e.message);

            // PRIORIDADE 3: Verificar cache local
            if (lidToNumberCache.has(sender)) {
              number = lidToNumberCache.get(sender);
              log("[MSG] ✅ Got real number from LID cache: " + number);
            } else {
              // FALLBACK: Usar LID
              number = sender.split("@")[0];
              log("[MSG] ⚠️ Using LID as number (fallback): " + number);
            }
          }
        }
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
        isLid: isLid,
      };

      if (hasMedia) {
        data.mediaType = messageType;
      }

      log(
        "[MSG] Sending to C#: from=" +
          senderName +
          ", number=" +
          number +
          (isLid ? " (LID)" : ""),
      );
      send({ type: "message", message: data });
    });
  } catch (error) {
    log("[BAILEYS] Error: " + error.message);
    log("[BAILEYS] Stack: " + error.stack);
  }
}

log("[BAILEYS] Server ready - waiting for plugin connection...");
