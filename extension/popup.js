// TDM Popup Script
const enabled = document.getElementById("enabled");
const overlay = document.getElementById("overlay");
const conn = document.getElementById("conn");
const ping = document.getElementById("ping");

function setConn(ok) {
    if (ok) {
        conn.textContent = "已连接";
        conn.className = "status ok";
    } else {
        conn.textContent = "未连接";
        conn.className = "status err";
    }
}

chrome.runtime.sendMessage({ type: "get-config" }, (resp) => {
    if (chrome.runtime.lastError) return;
    if (resp) {
        enabled.checked = resp.enabled !== false;
        overlay.checked = resp.overlay !== false;
        setConn(resp.connected);
    }
});

enabled.addEventListener("change", () => {
    chrome.runtime.sendMessage({ type: "set-config", enabled: enabled.checked });
});
overlay.addEventListener("change", () => {
    chrome.runtime.sendMessage({ type: "set-config", overlay: overlay.checked });
});

ping.addEventListener("click", () => {
    ping.textContent = "测试中…";
    chrome.runtime.sendMessage({ type: "ping" }, (resp) => {
        ping.textContent = "测试连接";
        if (chrome.runtime.lastError) {
            setConn(false);
            return;
        }
        setConn(resp && resp.connected);
    });
});
