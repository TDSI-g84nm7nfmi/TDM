// TDM Content Script
// 扫描页面中的媒体元素，并在它们旁边显示「TDM 下载」按钮
// 通过 background 转发到 native host

(function () {
    if (window.__TDM_INJECTED__) return;
    window.__TDM_INJECTED__ = true;

    const STYLE_ID = "tdm-overlay-style";
    const BTN_CLASS = "tdm-dl-btn";
    const WRAP_CLASS = "tdm-media-wrap";

    function injectStyle() {
        if (document.getElementById(STYLE_ID)) return;
        const link = document.createElement("link");
        link.id = STYLE_ID;
        link.rel = "stylesheet";
        link.href = chrome.runtime.getURL("overlay.css");
        document.documentElement.appendChild(link);
    }

    function getBestSrc(el) {
        // 视频：优先 source > src
        if (el.tagName === "VIDEO" || el.tagName === "AUDIO") {
            const sources = el.querySelectorAll("source");
            for (const s of sources) {
                const src = s.src || s.getAttribute("src");
                if (src && !src.startsWith("blob:")) return src;
            }
            if (el.currentSrc) return el.currentSrc;
            return el.src || "";
        }
        if (el.tagName === "IMG") {
            // 取最高分辨率
            if (el.srcset) {
                const candidates = el.srcset.split(",").map(s => s.trim().split(/\s+/));
                // 选最大宽度
                let best = candidates[0];
                for (const c of candidates) {
                    if (!c[0]) continue;
                    const w = parseInt((c[1] || "").match(/(\d+)w/)?.[1] || "0", 10);
                    const bw = parseInt((best[1] || "").match(/(\d+)w/)?.[1] || "0", 10);
                    if (w > bw) best = c;
                }
                if (best && best[0]) return best[0];
            }
            return el.currentSrc || el.src || "";
        }
        return el.src || el.getAttribute("href") || "";
    }

    function guessType(el, url) {
        if (el.tagName === "VIDEO") return "video";
        if (el.tagName === "AUDIO") return "audio";
        if (el.tagName === "IMG") return "image";
        const ext = (url.match(/\.([a-z0-9]{1,5})(\?|$)/i) || [, ""])[1].toLowerCase();
        if (["mp4", "webm", "m3u8", "ts", "mkv", "mov", "avi"].includes(ext)) return "video";
        if (["mp3", "wav", "ogg", "m4a", "flac", "aac"].includes(ext)) return "audio";
        if (["jpg", "jpeg", "png", "gif", "webp", "bmp", "svg", "avif"].includes(ext)) return "image";
        if (["pdf", "zip", "rar", "7z", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt"].includes(ext)) return "file";
        return "file";
    }

    function filenameOf(el, url) {
        if (el.tagName === "IMG" && el.alt) return el.alt.replace(/[\\/:*?"<>|]/g, "_");
        try {
            const u = new URL(url);
            const p = u.pathname;
            const fn = decodeURIComponent(p.substring(p.lastIndexOf("/") + 1));
            if (fn) return fn;
        } catch { }
        return `tdm_${Date.now()}`;
    }

    function makeButton(resource) {
        const btn = document.createElement("div");
        btn.className = BTN_CLASS;
        btn.title = `TDM 下载 (${resource.type})`;
        btn.innerHTML = `<svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3v12m0 0l-4-4m4 4l4-4M5 21h14"/></svg><span>TDM</span>`;
        btn.addEventListener("click", (e) => {
            e.stopPropagation();
            e.preventDefault();
            btn.classList.add("tdm-dl-busy");
            chrome.runtime.sendMessage({
                type: "send-resources",
                resources: [resource],
                pageUrl: location.href,
                pageTitle: document.title,
            }, (resp) => {
                btn.classList.remove("tdm-dl-busy");
                if (chrome.runtime.lastError || !resp || !resp.ok) {
                    btn.classList.add("tdm-dl-err");
                    btn.title = "TDM 未连接，请确认主程序已启动";
                    setTimeout(() => btn.classList.remove("tdm-dl-err"), 2000);
                } else {
                    btn.classList.add("tdm-dl-ok");
                    btn.title = "已发送到 TDM";
                    setTimeout(() => btn.classList.remove("tdm-dl-ok"), 1800);
                }
            });
        });
        return btn;
    }

    function wrapElement(el, resource) {
        if (el.dataset.tdmWrapped) return;
        el.dataset.tdmWrapped = "1";

        // 修复 inline 元素的包裹
        if (el.parentElement && el.parentElement.classList.contains(WRAP_CLASS)) return;

        const wrap = document.createElement("div");
        wrap.className = WRAP_CLASS + " tdm-pos";
        wrap.style.position = "relative";
        wrap.style.display = "inline-block";
        wrap.style.maxWidth = "100%";

        const parent = el.parentNode;
        if (!parent) return;
        parent.insertBefore(wrap, el);
        wrap.appendChild(el);
        wrap.appendChild(makeButton(resource));
    }

    function scan(root) {
        const imgs = (root || document).querySelectorAll("img, video, audio, picture source, a[href]");
        const found = [];
        for (const el of imgs) {
            if (el.tagName === "A" && !/(\.|\/)(zip|rar|7z|pdf|mp4|mp3|webm|mkv|avi|exe|msi|apk|docx?|xlsx?|pptx?)($|\?)/i.test(el.href || "")) continue;
            const url = getBestSrc(el);
            if (!url || url.startsWith("blob:") || url.startsWith("data:") || url.startsWith("#")) continue;
            // 跳过过小的图片（图标、占位）
            if (el.tagName === "IMG") {
                const w = el.naturalWidth || el.width;
                const h = el.naturalHeight || el.height;
                if (w > 0 && h > 0 && (w < 80 || h < 80)) continue;
            }
            const type = guessType(el, url);
            const filename = filenameOf(el, url);
            const resource = { url, type, filename };
            found.push(resource);
            if (el.tagName === "IMG" || el.tagName === "VIDEO" || el.tagName === "AUDIO") {
                wrapElement(el, resource);
            }
        }
        return found;
    }

    // 启动：等 DOM 准备好
    injectStyle();

    function start() {
        scan(document);

        // 节流观察
        let pending = false;
        const observer = new MutationObserver(() => {
            if (pending) return;
            pending = true;
            requestIdleCallback(() => {
                pending = false;
                scan(document);
            }, { timeout: 800 });
        });
        observer.observe(document.body || document.documentElement, { childList: true, subtree: true });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", start);
    } else {
        start();
    }

    // 接收后台消息：链接接管
    chrome.runtime.onMessage.addListener((msg) => {
        if (msg && msg.type === "tdm-takeover-link" && msg.url) {
            // 显示 toast 提示
            showToast(`TDM 已接管下载：${filenameOf({ tagName: "A" }, msg.url)}`);
            chrome.runtime.sendMessage({
                type: "send-resources",
                resources: [{ url: msg.url, type: guessType({ tagName: "A" }, msg.url), filename: filenameOf({ tagName: "A" }, msg.url) }],
                pageUrl: location.href,
                pageTitle: document.title,
            });
        }
    });

    function showToast(text) {
        let t = document.getElementById("tdm-toast");
        if (!t) {
            t = document.createElement("div");
            t.id = "tdm-toast";
            t.className = "tdm-toast";
            document.body.appendChild(t);
        }
        t.textContent = text;
        t.classList.add("tdm-toast-show");
        clearTimeout(t._timer);
        t._timer = setTimeout(() => t.classList.remove("tdm-toast-show"), 2400);
    }
})();
