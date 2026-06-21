#!/usr/bin/env python3
"""
TDM Native Messaging Host
接收浏览器扩展传来的资源消息，转发到 TDM 主程序（HTTP）。
消息协议：每个消息 = 4 字节长度（little-endian） + JSON 字节。
"""
import os
import sys
import json
import struct
import urllib.request
import urllib.error
import socket
import time
import threading

# ---------- 配置 ----------
TDM_HTTP = "http://127.0.0.1:8765"
LOG_PATH = os.path.join(os.path.expanduser("~"), "Downloads", "TDM", "native_host.log")
os.makedirs(os.path.dirname(LOG_PATH), exist_ok=True)


def log(msg):
    line = f"[{time.strftime('%H:%M:%S')}] {msg}\n"
    try:
        with open(LOG_PATH, "a", encoding="utf-8") as f:
            f.write(line)
    except Exception:
        pass
    try:
        sys.stderr.write(line)
        sys.stderr.flush()
    except Exception:
        pass


# ---------- HTTP 转发 ----------
def post_resources(resources, kind="media-detected", page_url=None, page_title=None):
    payload = {"type": kind, "resources": resources}
    if page_url:
        payload["pageUrl"] = page_url
    if page_title:
        payload["pageTitle"] = page_title
    data = json.dumps(payload).encode("utf-8")
    for attempt in range(2):
        try:
            req = urllib.request.Request(
                TDM_HTTP + "/api/resources",
                data=data,
                headers={"Content-Type": "application/json"},
                method="POST",
            )
            with urllib.request.urlopen(req, timeout=4) as r:
                body = r.read().decode("utf-8", errors="ignore")
                log(f"OK {kind} -> TDM ({len(resources)} items): {body[:200]}")
                return True, body
        except (urllib.error.URLError, socket.timeout, ConnectionRefusedError, OSError) as e:
            log(f"HTTP 失败 (尝试 {attempt + 1}): {e}")
            time.sleep(0.4)
    return False, "TDM 未运行"


def post_download_item(item):
    return post_resources([item], kind="download")


def post_media_list(items, page_url, page_title):
    return post_resources(items, kind="media-detected", page_url=page_url, page_title=page_title)


# ---------- 消息收发 ----------
def read_message():
    """读取单条 native message。"""
    raw_len = sys.stdin.buffer.read(4)
    if len(raw_len) < 4:
        return None
    length = struct.unpack("<I", raw_len)[0]
    if length == 0 or length > 1024 * 1024:
        return None
    data = sys.stdin.buffer.read(length)
    if len(data) < length:
        return None
    try:
        return json.loads(data.decode("utf-8"))
    except Exception as e:
        log(f"JSON 解析失败: {e}")
        return None


def write_message(obj):
    """回写一条 native message。"""
    data = json.dumps(obj).encode("utf-8")
    sys.stdout.buffer.write(struct.pack("<I", len(data)))
    sys.stdout.buffer.write(data)
    sys.stdout.buffer.flush()


# ---------- 处理 ----------
def handle(msg):
    if not isinstance(msg, dict):
        return {"ok": False, "error": "invalid message"}
    t = msg.get("type")
    if t == "ping":
        return {"ok": True, "type": "ack", "ts": int(time.time() * 1000)}
    if t == "download":
        items = msg.get("resources") or []
        if not items:
            return {"ok": False, "error": "no resources"}
        ok, body = post_download_item(items[0])
        return {"ok": ok, "type": "ack", "echo": body[:200] if body else ""}
    if t == "media-detected":
        items = msg.get("resources") or []
        if not items:
            return {"ok": False, "error": "no resources"}
        ok, body = post_media_list(items, msg.get("pageUrl"), msg.get("pageTitle"))
        return {"ok": ok, "type": "ack", "echo": body[:200] if body else ""}
    return {"ok": False, "error": f"unknown type: {t}"}


def main():
    log("native_host 启动")
    while True:
        try:
            msg = read_message()
            if msg is None:
                log("stdin 关闭，退出")
                break
            log(f"RX: {json.dumps(msg)[:300]}")
            resp = handle(msg)
            try:
                write_message(resp)
            except Exception as e:
                log(f"回写失败: {e}")
                break
        except Exception as e:
            log(f"循环异常: {e}")
            break
    log("native_host 退出")


if __name__ == "__main__":
    main()
