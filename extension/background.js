// TDM Background Service Worker
// 通过 WebSocket 与 TDM 主程序通信（替代 Native Messaging）
const WS_URL = "ws://127.0.0.1:19199/tdm";
let ws = null;
let wsReconnectTimer = null;
let wsId = 0;       // 递增消息 ID

function wsConnect() {
    try {
        ws = new WebSocket(WS_URL);
        ws.onopen = () => {
            // console.log("[TDM] WebSocket 已连接");
            wsReconnectTimer = null;
        };
        ws.onmessage = (event) => {
            try {
                const msg = JSON.parse(event.data);
                handleWsMessage(msg);
            } catch (e) { /* ignore parse errors */ }
        };
        ws.onclose = () => {
            ws = null;
            wsScheduleReconnect();
        };
        ws.onerror = () => {
            // onerror 后会自动触发 onclose
        };
    } catch (e) {
        wsScheduleReconnect();
    }
}

function wsScheduleReconnect() {
    if (wsReconnectTimer) return;
    wsReconnectTimer = setTimeout(() => {
        wsReconnectTimer = null;
        wsConnect();
    }, 3000);
}

// 等待连接的 promise
function wsWaitConnected(timeout = 5000) {
    return new Promise((resolve) => {
        if (ws && ws.readyState === WebSocket.OPEN) { resolve(true); return; }
        const start = Date.now();
        const check = setInterval(() => {
            if (ws && ws.readyState === WebSocket.OPEN) { clearInterval(check); resolve(true); }
            else if (Date.now() - start > timeout) { clearInterval(check); resolve(false); }
        }, 200);
    });
}

function wsSend(payload) {
    return new Promise((resolve) => {
        wsWaitConnected().then((ok) => {
            if (!ok || !ws) { resolve({ ok: false, error: "WebSocket 未连接" }); return; }
            const id = ++wsId;
            payload._id = id;
            // 等待响应（消息路由到 handleWsMessage）
            const timeout = setTimeout(() => resolve({ ok: false, error: "超时" }), 5000);
            const handler = (msg) => {
                if (msg && msg._id === id) {
                    clearTimeout(timeout);
                    resolve(msg);
                    return true;
                }
                return false;
            };
            _pendingResponses.set(id, handler);
            try {
                ws.send(JSON.stringify(payload));
            } catch (e) {
                _pendingResponses.delete(id);
                clearTimeout(timeout);
                resolve({ ok: false, error: e.message });
            }
        });
    });
}

// 挂起的响应路由
const _pendingResponses = new Map();

function handleWsMessage(msg) {
    if (msg && msg._id && _pendingResponses.has(msg._id)) {
        const handler = _pendingResponses.get(msg._id);
        _pendingResponses.delete(msg._id);
        handler(msg);
        return;
    }
    // 其他服务端推送的消息
    if (msg && msg.type === "connected") {
        // console.log("[TDM] WS 握手成功", msg);
    }
}

// 启动连接
wsConnect();

// ---------- 存储 ----------
async function getEnabled() {
    return new Promise((r) => {
        chrome.storage.local.get([STORAGE_KEYS.enabled], (v) => {
            r(v[STORAGE_KEYS.enabled] !== false); // 默认开启
        });
    });
}

async function getShowOverlay() {
    return new Promise((r) => {
        chrome.storage.local.get([STORAGE_KEYS.showOverlay], (v) => {
            r(v[STORAGE_KEYS.showOverlay] !== false); // 默认开启
        });
    });
}

// ---------- 类型推断 ----------
function guessTypeFromUrl(url) {
    try {
        const u = new URL(url);
        const path = u.pathname.toLowerCase();
        const ext = (path.match(/\.([a-z0-9]{1,6})$/) || [, ""])[1];
        if (["jpg", "jpeg", "png", "gif", "webp", "bmp", "svg", "avif", "tiff", "ico"].includes(ext)) return "image";
        if (["mp4", "webm", "mkv", "avi", "mov", "flv", "m3u8", "ts", "m4v"].includes(ext)) return "video";
        if (["mp3", "wav", "flac", "ogg", "m4a", "aac", "opus"].includes(ext)) return "audio";
        if (["pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "zip", "rar", "7z", "tar", "gz", "iso", "exe", "msi"].includes(ext)) return "file";
        return null;
    } catch { return null; }
}

function filenameFromUrl(url) {
    try {
        const u = new URL(url);
        const p = u.pathname;
        const fn = decodeURIComponent(p.substring(p.lastIndexOf("/") + 1));
        return fn || `download_${Date.now()}`;
    } catch {
        return `download_${Date.now()}`;
    }
}

// 1) 拦截所有 chrome.downloads 事件
chrome.downloads.onCreated.addListener(async (downloadItem) => {
    if (!(await getEnabled())) return;
    // 标记为 TDM 接管，避免递归
    if (downloadItem.byExtensionId === chrome.runtime.id) return;

    const url = downloadItem.finalUrl || downloadItem.url;
    if (!url || url.startsWith("blob:") || url.startsWith("data:")) return;

    // 取消浏览器自身下载
    try {
        await chrome.downloads.cancel(downloadItem.id);
    } catch (e) { /* ignore */ }

    const type = guessTypeFromUrl(url) || "file";
    wsSend({
        type: "download",
        resources: [
            {
                url,
                filename: downloadItem.filename ? downloadItem.filename.replace(/^.*[\\/]/, "") : filenameFromUrl(url),
                mime: type,
                referrer: downloadItem.referrer,
                source: "browser-download",
            },
        ],
    });
});

// 2) webRequest 拦截：捕获服务器返回的可下载类型响应
const DOWNLOAD_PATH = /.(zip|rar|7z|tar|gz|iso|exe|msi|pdf|docx?|xlsx?|pptx?|mp4|webm|mkv|avi|mov|flv|m3u8|ts|mp3|wav|flac|ogg|m4a|aac|apk|dmg|pkg)$/i;
chrome.webRequest.onHeadersReceived.addListener(
    (details) => {
        // 暂只做检测，不重复发送
    },
    { urls: ["<all_urls>"], types: ["main_frame", "sub_frame", "other"] },
    ["responseHeaders"]
);

// 3) 拦截直接点击下载链接（a[download] 或带可下载扩展名）
chrome.webRequest.onBeforeRequest.addListener(
    (details) => {
        if (details.tabId < 0) return;
        // 只看 main_frame / sub_frame
        if (!["main_frame", "sub_frame", "other"].includes(details.type)) return;
        if (DOWNLOAD_PATH.test(details.url)) {
            // 通过 content script 提示接管
            chrome.tabs.sendMessage(details.tabId, {
                type: "tdm-takeover-link",
                url: details.url,
            }).catch(() => {});
        }
    },
    { urls: ["<all_urls>"] }
);

// 4) 来自 content script 的媒体资源
chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
    if (!msg || !msg.type) return;
    if (msg.type === "send-resources") {
        if (msg.resources && msg.resources.length) {
            wsSend({
                type: "media-detected",
                resources: msg.resources,
                pageUrl: msg.pageUrl,
                pageTitle: msg.pageTitle,
            }).then(sendResponse);
            return true;
        }
    } else if (msg.type === "ping") {
        sendResponse({ ok: true, connected: ws && ws.readyState === WebSocket.OPEN });
        return false;
    } else if (msg.type === "get-config") {
        Promise.all([getEnabled(), getShowOverlay()]).then(([enabled, overlay]) => {
            sendResponse({ ok: true, enabled, overlay, connected: ws && ws.readyState === WebSocket.OPEN });
        });
        return true;
    } else if (msg.type === "set-config") {
        chrome.storage.local.set({ [STORAGE_KEYS.enabled]: !!msg.enabled, [STORAGE_KEYS.showOverlay]: !!msg.overlay }, () => {
            sendResponse({ ok: true });
        });
        return true;
    }
    return false;
});

console.log("[TDM] background ready");
