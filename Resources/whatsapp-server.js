/**
 * WhatsApp SimHub Plugin - WhatsApp Web.js Backend
 * @version 1.0.2
 *
 * CHANGELOG v1.0.2:
 * - Dynamic port selection (finds available port automatically)
 * - Outputs PORT:XXXX to stdout for C# to read
 *
 * CHANGELOG v1.0.1:
 * - Fixed race condition where LocalAuth completes before C# connects
 * - Added pendingReadyData to cache ready state
 * - Added pendingContactsList to cache contacts list
 * - WebSocket connection now resends ready + contacts if already available
 */
const SCRIPT_VERSION = "1.0.2";

const { Client, LocalAuth } = require("whatsapp-web.js");
const WebSocket = require("ws");
const fs = require("fs");
const path = require("path");
const os = require("os");
const { execSync } = require("child_process");

const appData =
  process.env.APPDATA || path.join(os.homedir(), "AppData", "Roaming");
const dataPath = path.join(appData, "SimHub", "WhatsAppPlugin", "data");
const logPath = path.join(
  appData,
  "SimHub",
  "WhatsAppPlugin",
  "logs",
  "node.log",
);

try {
  const logDir = path.dirname(logPath);
  if (!fs.existsSync(logDir)) fs.mkdirSync(logDir, { recursive: true });
  fs.writeFileSync(logPath, "");
} catch (e) {}

function log(msg) {
  const line = "[" + new Date().toISOString().substring(11, 23) + "] " + msg;

  // Try console.log, but ignore EPIPE errors (broken pipe)
  try {
    console.log(line);
  } catch (e) {
    // Ignore EPIPE errors - happens when stdout is closed
    if (e.code !== "EPIPE") {
      // Re-throw other errors
      throw e;
    }
  }

  // Always try to write to log file
  try {
    fs.appendFileSync(logPath, line + "\n");
  } catch (e) {
    // Ignore file write errors
  }
}

// Prevent infinite loops in error handlers
let isHandlingError = false;

process.on("uncaughtException", (err) => {
  // Ignore EPIPE errors completely - they're expected when stdout closes
  if (err.code === "EPIPE") {
    return;
  }

  // Prevent recursive error handling
  if (isHandlingError) {
    return;
  }

  isHandlingError = true;

  try {
    log("[FATAL] Uncaught exception: " + err.message);
    log("[STACK] " + err.stack);
  } catch (e) {
    // If logging fails, write directly to file
    try {
      fs.appendFileSync(
        logPath,
        "[FATAL] Error in error handler: " + e.message + "\n",
      );
    } catch (e2) {
      // Nothing more we can do
    }
  }

  isHandlingError = false;
});

process.on("unhandledRejection", (err) => {
  if (isHandlingError) {
    return;
  }

  isHandlingError = true;

  try {
    log("[FATAL] Unhandled rejection: " + err);
  } catch (e) {
    try {
      fs.appendFileSync(logPath, "[FATAL] Unhandled rejection: " + err + "\n");
    } catch (e2) {
      // Nothing more we can do
    }
  }

  isHandlingError = false;
});

/**
 * Find an available port by checking which ports are in use via netstat
 * @param {number} startPort - Port to start searching from
 * @param {number} endPort - Port to stop searching at
 * @returns {number} - Available port number
 */
function findAvailablePort(startPort = 3000, endPort = 3100) {
  try {
    // Get list of ports in use on localhost
    const netstatOutput = execSync("netstat -ano", { encoding: "utf8" });
    const lines = netstatOutput.split("\n");

    const usedPorts = new Set();

    for (const line of lines) {
      // Match lines with 127.0.0.1:PORT or 0.0.0.0:PORT (LISTENING)
      const match = line.match(/(?:127\.0\.0\.1|0\.0\.0\.0):(\d+)/);
      if (match) {
        usedPorts.add(parseInt(match[1], 10));
      }
    }

    // Find first available port in range
    for (let port = startPort; port <= endPort; port++) {
      if (!usedPorts.has(port)) {
        return port;
      }
    }

    // Fallback to startPort if all are "in use" (unlikely)
    return startPort;
  } catch (error) {
    // If netstat fails, just return startPort and let the server try
    log("[PORT] Warning: Could not check ports via netstat: " + error.message);
    return startPort;
  }
}

// Find available port
const selectedPort = findAvailablePort(3000, 3100);

// ⭐ IMPORTANT: Output port for C# to read (must be before any other output)
console.log("PORT:" + selectedPort);

log("[WS] Script version: " + SCRIPT_VERSION);
log("[WS] Starting server on port " + selectedPort + "...");

const wss = new WebSocket.Server({
  port: selectedPort,
  host: "127.0.0.1",
});

let ws = null;

// ⭐ FIX: Cache para dados que podem chegar antes do C# conectar
let pendingReadyData = null; // Guardar estado ready
let pendingContactsList = null; // Guardar lista de contactos

wss.on("error", (error) => {
  log("[WS] Server error: " + error.message);
});

wss.on("listening", () => {
  log("[WS] Server successfully listening on 127.0.0.1:" + selectedPort);
});

function send(data) {
  if (ws && ws.readyState === WebSocket.OPEN) {
    try {
      ws.send(JSON.stringify(data));
      log("[WS] Sent: " + data.type);
      return true; // ⭐ Indicar sucesso
    } catch (err) {
      log("[WS] Send error: " + err.message);
      return false;
    }
  } else {
    log("[WS] Cannot send - no connection (will retry when C# connects)");
    return false; // ⭐ Indicar falha
  }
}

wss.on("connection", (socket) => {
  log("[WS] Plugin connected!");
  ws = socket;

  // ⭐ FIX: SE JÁ ESTIVER READY, ENVIAR IMEDIATAMENTE!
  if (isReady && pendingReadyData) {
    log("[WS] ⭐ Client already ready - sending cached ready state to plugin");
    send(pendingReadyData);

    // Também enviar lista de contactos se já tivermos
    if (pendingContactsList) {
      log(
        "[WS] ⭐ Sending cached contacts list (" +
          pendingContactsList.contacts.length +
          " contacts)",
      );
      send(pendingContactsList);
    }
  } else {
    log("[WS] Client not ready yet - will send ready when WhatsApp connects");
  }

  socket.on("message", async (msg) => {
    try {
      const data = JSON.parse(msg);
      log("[WS] Received: " + data.type);

      if (data.type === "shutdown") {
        log("[WA] Graceful shutdown initiated...");
        client.destroy().then(() => {
          log("[WA] Client destroyed. Exiting.");
          process.exit(0);
        });
      } else if (data.type === "sendReply") {
        try {
          log(`[REPLY] 📤 Sending to: ${data.chatId}`);
          log(`[REPLY] 📝 Text: ${data.text}`);

          // 🚀 ENVIAR DIRETO PELA API RAW DO WHATSAPP STORE
          // Bypassa TUDO do whatsapp-web.js
          const result = await client.pupPage.evaluate(
            async (chatId, messageText) => {
              try {
                // 1. Obter o chat
                const chat = await window.Store.Chat.find(chatId);
                if (!chat) {
                  return { success: false, error: `Chat not found: ${chatId}` };
                }

                // 2. Criar mensagem usando a API interna
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

          log(`[REPLY] ✅ Message sent successfully! ID: ${result.messageId}`);
          log("[REPLY] ✅✅✅ COMPLETE SUCCESS! ✅✅✅");
        } catch (error) {
          log(`[REPLY] ❌ FAILED: ${error.message}`);
          throw error;
        }
      } else if (data.type === "refreshChatContacts") {
        // 🔄 Refresh manual de contactos das conversas
        log("[CHATS] Manual refresh requested...");

        client
          .getChats()
          .then((chats) => {
            const validContacts = [];

            for (const chat of chats) {
              if (chat.isGroup) continue;

              const id = chat.id?.user || chat.id?._serialized;
              const name = chat.name || "(No name)";

              if (id) {
                validContacts.push({
                  name: name,
                  number: id,
                });
              }
            }

            validContacts.sort((a, b) => a.name.localeCompare(b.name));

            log("[CHATS] Refreshed " + validContacts.length + " contacts");

            // ⭐ Guardar em cache também
            pendingContactsList = {
              type: "chatContactsList",
              contacts: validContacts,
            };
            send(pendingContactsList);
          })
          .catch((error) => {
            log("[CHATS ERROR] Refresh failed: " + error.message);
            send({ type: "chatContactsError", error: error.message });
          });
      } else if (data.type === "getStatus") {
        // ⭐ NOVO: Permitir C# pedir estado atual
        log("[STATUS] Status request received");
        if (isReady && pendingReadyData) {
          send(pendingReadyData);
          if (pendingContactsList) {
            send(pendingContactsList);
          }
        } else {
          send({
            type: "status",
            ready: false,
            message: "WhatsApp not ready yet",
          });
        }
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

log("[WA] Data path: " + dataPath);
if (!fs.existsSync(dataPath)) {
  fs.mkdirSync(dataPath, { recursive: true });
  log("[WA] Created data dir");
}

log("[WA] Creating client...");

const client = new Client({
  authStrategy: new LocalAuth({ clientId: "simhub", dataPath: dataPath }),
  puppeteer: {
    headless: true,
    args: ["--no-sandbox", "--disable-setuid-sandbox"],
  },
  // 🔧 FIX: Force LID migration to prevent infinite logout (GitHub #3856)
  authTimeoutMs: 60000, // Increased timeout for auth
  webVersionCache: {
    type: "remote",
    remotePath:
      "https://raw.githubusercontent.com/wppconnect-team/wa-version/main/html/2.2412.54.html",
  },
});

log("[WA] Client created");

log("[DEBUG] Registering ALL possible events...");

client.on("loading_screen", (percent, message) => {
  log("[LOADING] " + percent + "% - " + message);
});

client.on("qr", (qr) => {
  log("[QR] Generated");
  send({ type: "qr", qr: qr });
});

client.on("auth_failure", (msg) => {
  log("[AUTH] Authentication failure: " + msg);
});

log("[STRATEGY] Using HYBRID approach:");
log("[STRATEGY] 1. Try EVENTS first (fork should fix them)");
log("[STRATEGY] 2. Fallback to POLLING if events fail");

let seenMessages = new Set();
let isReady = false;
let pollingInterval = null;
let eventsWorking = false;
let eventsCheckTimeout = null;
let readyTimestamp = 0; // ⭐ TIMESTAMP quando ficou ready
let oldMessagesIgnoredCount = 0; // 🆕 Contador para evitar spam

async function processMessage(msg) {
  try {
    if (seenMessages.has(msg.id._serialized)) return;
    if (msg.fromMe) return;

    // ⭐ SÓ PROCESSAR MENSAGENS APÓS READY!
    if (readyTimestamp > 0 && msg.timestamp * 1000 < readyTimestamp) {
      oldMessagesIgnoredCount++;
      // Só logar as primeiras 20 para não fazer spam
      if (oldMessagesIgnoredCount <= 20) {
        log(
          "[MSG] IGNORED - message too old (before ready) [" +
            oldMessagesIgnoredCount +
            "]",
        );
      } else if (oldMessagesIgnoredCount === 21) {
        log(
          "[MSG] IGNORED - stopping logs (too many old messages, " +
            oldMessagesIgnoredCount +
            "+ total)",
        );
      }
      return;
    }

    seenMessages.add(msg.id._serialized);

    log("[MSG] NEW from: " + msg.from);
    log("[MSG] Body: " + (msg.body || "(no text)"));

    // 🔍 DEBUG: MOSTRAR TODAS AS PROPRIEDADES!
    log("[DEBUG] =================== MESSAGE OBJECT DUMP ===================");

    // Propriedades principais
    log("[DEBUG] msg.from: " + msg.from);
    log("[DEBUG] msg.to: " + msg.to);
    log("[DEBUG] msg.author: " + msg.author);
    log("[DEBUG] msg.notifyName: " + msg.notifyName);

    // ID object
    try {
      log("[DEBUG] --- msg.id ---");
      log("[DEBUG] msg.id._serialized: " + msg.id._serialized);
      log("[DEBUG] msg.id.remote: " + msg.id.remote);
      log("[DEBUG] msg.id.id: " + msg.id.id);
      log("[DEBUG] msg.id.fromMe: " + msg.id.fromMe);
      if (msg.id.participant)
        log("[DEBUG] msg.id.participant: " + msg.id.participant);

      // Mostrar TODAS as keys do id
      const idKeys = Object.keys(msg.id);
      log("[DEBUG] msg.id ALL KEYS: " + idKeys.join(", "));
    } catch (e) {
      log("[DEBUG] Error reading msg.id: " + e.message);
    }

    // _data object (dados internos)
    try {
      if (msg._data) {
        log("[DEBUG] --- msg._data ---");
        log("[DEBUG] msg._data.from: " + msg._data.from);
        log("[DEBUG] msg._data.to: " + msg._data.to);

        // Procurar propriedades com "sender", "contact", "number", "user"
        const dataKeys = Object.keys(msg._data);
        log("[DEBUG] msg._data ALL KEYS: " + dataKeys.join(", "));

        // Verificar sender
        if (msg._data.sender) {
          log("[DEBUG] msg._data.sender FOUND!");
          const senderKeys = Object.keys(msg._data.sender);
          log("[DEBUG] msg._data.sender KEYS: " + senderKeys.join(", "));

          // Tentar propriedades comuns
          if (msg._data.sender.id)
            log(
              "[DEBUG] msg._data.sender.id: " +
                JSON.stringify(msg._data.sender.id),
            );
          if (msg._data.sender.user)
            log("[DEBUG] msg._data.sender.user: " + msg._data.sender.user);
          if (msg._data.sender.number)
            log("[DEBUG] msg._data.sender.number: " + msg._data.sender.number);
        }

        // Verificar participant
        if (msg._data.participant) {
          log("[DEBUG] msg._data.participant FOUND!");
          log(
            "[DEBUG] msg._data.participant: " +
              JSON.stringify(msg._data.participant),
          );
        }
      }
    } catch (e) {
      log("[DEBUG] Error reading msg._data: " + e.message);
    }

    // Tentar getContact() para comparar
    try {
      log("[DEBUG] --- Trying getContact() ---");
      const contact = await msg.getContact();
      if (contact) {
        const contactKeys = Object.keys(contact);
        log("[DEBUG] contact ALL KEYS: " + contactKeys.join(", "));
        if (contact.id) {
          log("[DEBUG] contact.id._serialized: " + contact.id._serialized);
          const contactIdKeys = Object.keys(contact.id);
          log("[DEBUG] contact.id ALL KEYS: " + contactIdKeys.join(", "));
          if (contact.id.user)
            log("[DEBUG] contact.id.user: " + contact.id.user);
          if (contact.id._serialized)
            log("[DEBUG] contact.id._serialized: " + contact.id._serialized);
        }
        if (contact.number) log("[DEBUG] contact.number: " + contact.number);
        if (contact.pushname)
          log("[DEBUG] contact.pushname: " + contact.pushname);
        if (contact.name) log("[DEBUG] contact.name: " + contact.name);
      }
    } catch (e) {
      log("[DEBUG] getContact() failed: " + e.message);
    }

    log("[DEBUG] ============================================================");

    // ⭐ OBTER NÚMERO REAL (3 MÉTODOS)
    let number = null;
    let name = msg.notifyName || msg.author || "Unknown";

    // ✅ MÉTODO 1: Se é @c.us, extrair direto (número real!)
    if (msg.from.includes("@c.us")) {
      number = msg.from.split("@")[0];
      log("[MSG] ✅ Direct number from @c.us: " + number);
    }

    // ⚠️ MÉTODO 2-4: Se é @lid, tentar obter número real
    else if (msg.from.includes("@lid")) {
      log("[MSG] ⚠️ LinkedID detected, trying to get real number...");

      // MÉTODO 2: getContactById
      try {
        log("[MSG] Method 1: Trying client.getContactById()...");
        const contact = await client.getContactById(msg.from);
        if (contact && contact.number) {
          number = contact.number;
          log("[MSG] ✅ Got number from getContactById: " + number);
          if (contact.pushname) name = contact.pushname;
          else if (contact.name) name = contact.name;
        }
      } catch (err) {
        log("[MSG] ❌ getContactById failed: " + err.message);
      }

      // MÉTODO 3: getChats
      if (!number) {
        try {
          log("[MSG] Method 2: Trying client.getChats()...");
          const chats = await client.getChats();
          const chat = chats.find((c) => c.id._serialized === msg.from);
          if (chat && chat.contact) {
            if (chat.contact.number) {
              number = chat.contact.number;
              log("[MSG] ✅ Got number from getChats: " + number);
            }
            if (chat.contact.pushname) name = chat.contact.pushname;
            else if (chat.contact.name) name = chat.contact.name;
          }
        } catch (err) {
          log("[MSG] ❌ getChats failed: " + err.message);
        }
      }

      // MÉTODO 4: Direct Store access via page.evaluate
      if (!number) {
        try {
          log("[MSG] Method 3: Trying direct Store access...");
          const storeNumber = await client.pupPage.evaluate((chatId) => {
            try {
              const Store = window.Store;
              if (Store && Store.Contact) {
                const contact = Store.Contact.get(chatId);
                if (contact && contact.id && contact.id.user) {
                  return contact.id.user;
                }
              }
            } catch (e) {
              return null;
            }
            return null;
          }, msg.from);

          if (storeNumber) {
            number = storeNumber;
            log("[MSG] ✅ Got number from Store: " + number);
          }
        } catch (err) {
          log("[MSG] ❌ Store access failed: " + err.message);
        }
      }

      // ❌ TODOS OS MÉTODOS FALHARAM!
      if (!number) {
        log("[MSG] ❌ CRITICAL: Could not get real number from LinkedID!");
        log("[MSG] ❌ All 3 methods failed. Message will be REJECTED.");
        log("[MSG] ❌ Possible solutions:");
        log(
          "[MSG]    1. Save contact in your phone with full number (+351...)",
        );
        log("[MSG]    2. Ask contact to message you first");
        log("[MSG]    3. Add contact to your WhatsApp on phone");
        return; // ← REJEITAR!
      }
    }

    // ❌ Formato desconhecido
    else {
      log("[MSG] ❌ REJECTED - Unknown format: " + msg.from);
      return;
    }

    log("[MSG] ✅ Final: number=" + number + ", name=" + name);

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
        log("[MSG] Media processed: " + type);
      } catch (err) {
        log("[MSG] Media error: " + err.message);
      }
    }

    log("[MSG] 📤 SENDING TO C#: from=" + name + ", number=" + number);
    send({ type: "message", message: data });
  } catch (err) {
    log("[MSG] Process error: " + err.message);
  }
}

function startPolling() {
  if (pollingInterval) {
    log("[POLL] Already running, skipping...");
    return;
  }

  log("[POLL] Starting POLLING mode (backup)...");
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
        } catch (err) {
          log("[POLL] Chat error: " + err.message);
        }
      }
    } catch (err) {
      log("[POLL] ERROR: " + err.message);
    }
  }, 5000);

  log("[POLL] Polling started (5s interval)");
}

client.on("message_create", async (msg) => {
  log("[EVENT] message_create fired! Events are WORKING!");
  eventsWorking = true;

  if (eventsCheckTimeout) {
    clearTimeout(eventsCheckTimeout);
    eventsCheckTimeout = null;
    log("[EVENT] Cancelled polling fallback - using EVENTS!");
  }

  await processMessage(msg);
});

client.on("authenticated", () => {
  log("[AUTH] Authenticated!");
});

client.on("ready", async () => {
  log("[READY] Client ready!");

  // 🔧 FIX: Force LID migration to prevent infinite logout (GitHub #3856)
  try {
    log("[LID-FIX] Injecting LID migration fix...");
    await client.pupPage.evaluate(() => {
      window.Store.Cmd.default.isLidMigrated = () => true;
    });
    log("[LID-FIX] ✅ LID migration fix applied successfully!");
  } catch (err) {
    log("[LID-FIX] ⚠️ Could not apply fix (may not be needed): " + err.message);
  }

  // ⭐ MARCAR TIMESTAMP DE READY
  readyTimestamp = Date.now();
  log(
    "[READY] Timestamp set to: " +
      readyTimestamp +
      " (only process messages after this)",
  );

  // ⭐ FIX: Guardar dados do ready em cache E enviar
  if (client.info && client.info.wid) {
    const number = client.info.wid.user;
    const name = client.info.pushname || "User";
    log("[READY] Number: " + number + ", Name: " + name);

    // Guardar em cache (para quando C# conectar depois)
    pendingReadyData = { type: "ready", number: number, name: name };

    // Tentar enviar (pode falhar se C# ainda não conectou - não há problema!)
    const sent = send(pendingReadyData);
    if (!sent) {
      log("[READY] ⚠️ Could not send ready now - will send when C# connects");
    }
  }

  isReady = true;

  // 📱 OBTER CONTACTOS DAS CONVERSAS ATIVAS
  log("[CHATS] Fetching contacts from active conversations...");

  client
    .getChats()
    .then((chats) => {
      log("[CHATS] Total chats retrieved: " + chats.length);

      // Filtrar só chats individuais (não grupos)
      const validContacts = [];

      for (const chat of chats) {
        if (chat.isGroup) continue; // Ignorar grupos

        // ID do chat = número
        const id = chat.id?.user || chat.id?._serialized;
        const name = chat.name || "(No name)";

        if (id) {
          validContacts.push({
            name: name,
            number: id, // Já vem sem + (ex: 351910203114)
          });
        }
      }

      // Ordenar por nome
      validContacts.sort((a, b) => a.name.localeCompare(b.name));

      log("[CHATS] Valid contacts from chats: " + validContacts.length);

      // ⭐ FIX: Guardar em cache E enviar
      pendingContactsList = {
        type: "chatContactsList",
        contacts: validContacts,
      };

      const sent = send(pendingContactsList);
      if (!sent) {
        log(
          "[CHATS] ⚠️ Could not send contacts now - will send when C# connects",
        );
      } else {
        log("[CHATS] Contacts list sent to plugin!");
      }
    })
    .catch((error) => {
      log("[CHATS ERROR] Failed to get chats: " + error.message);
      send({
        type: "chatContactsError",
        error: error.message,
      });
    });

  // ✅ WhatsApp conectado e pronto!
  log("[READY] Testing EVENTS for 30 seconds...");
  log("[READY] If no events fire, will fallback to POLLING");

  eventsCheckTimeout = setTimeout(() => {
    if (!eventsWorking) {
      log("[FALLBACK] No events fired after 30s - starting POLLING!");
      startPolling();
    } else {
      log("[SUCCESS] Events are working - NO POLLING needed!");
    }
  }, 30000);
});

client.on("disconnected", (reason) => {
  log("[WA] Disconnected: " + reason);

  // ⭐ Limpar cache quando desconecta
  isReady = false;
  pendingReadyData = null;
  pendingContactsList = null;

  send({ type: "disconnected", reason: reason });
});

log("[WA] Initializing...");
client.initialize();
