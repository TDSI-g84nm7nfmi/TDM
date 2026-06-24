import paramiko
import os

HOST = "192.168.5.12"
USER = "ztl"
PASS = "123456"

ssh = paramiko.SSHClient()
ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
ssh.connect(HOST, username=USER, password=PASS)
sftp = ssh.open_sftp()

# 1. 更新 TDM 页面 - 在安装程序链接处添加下载
print("=== 更新 TDM 页面 ===")
with sftp.open("/var/www/tdm/index.html", "r") as f:
    tdm_html = f.read().decode("utf-8", errors="replace")

# 查找下载按钮/链接区域，替换为新的安装程序链接
import re

# 如果有旧的 exe 下载链接，替换为新的
old_links = [
    "TDM-Setup.exe",
    "TDM_Setup.exe",
    "tdm-setup.exe",
    "TDM.exe",
]
for old in old_links:
    tdm_html = tdm_html.replace(old, "TDSI-Download-Manager-Setup.exe")

# 确保有安装程序下载链接
if "TDSI-Download-Manager-Setup.exe" not in tdm_html:
    # 在第一个 download 按钮处添加
    tdm_html = tdm_html.replace(
        'href="',
        'href="TDSI-Download-Manager-Setup.exe"',
        1
    )

with sftp.open("/var/www/tdm/index.html", "w") as f:
    f.write(tdm_html.encode("utf-8"))
print("TDM 页面已更新")

# 2. 更新下载页面
print("=== 更新下载页面 ===")
with sftp.open("/var/www/tdsi/download.html", "r") as f:
    dl_html = f.read().decode("utf-8", errors="replace")

# 替换旧的安装程序链接
for old in old_links:
    dl_html = dl_html.replace(old, "TDSI-Download-Manager-Setup.exe")

# 更新版本信息
dl_html = dl_html.replace("v0.", "v1.")
dl_html = dl_html.replace("版本 0", "版本 1")

with sftp.open("/var/www/tdsi/download.html", "w") as f:
    f.write(dl_html.encode("utf-8"))
print("下载页面已更新")

# 3. 同时上传到 tdsi 目录（方便从主站下载）
print("=== 复制安装程序到 tdsi 目录 ===")
try:
    sftp.put(
        r"C:\Users\Administrator\Desktop\TDSI服务器官网\downloader\dist\TDSI-Download-Manager-Setup.exe",
        "/var/www/tdsi/TDSI-Download-Manager-Setup.exe"
    )
    print("已复制到 /var/www/tdsi/")
except Exception as e:
    print(f"复制失败: {e}")

sftp.close()
ssh.close()
print("\n全部完成!")
