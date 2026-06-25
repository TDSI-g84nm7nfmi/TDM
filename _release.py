import subprocess, json, os, requests

# 从 git credential-manager 获取 GitHub token
proc = subprocess.run(
    ["git", "credential-manager", "get"],
    input="protocol=https\nhost=github.com\n\n",
    capture_output=True, text=True
)
print("credential output:", proc.stdout[:200])

token = None
for line in proc.stdout.splitlines():
    if line.startswith("password="):
        token = line.split("=", 1)[1]
        break

if not token:
    print("无法获取 token，尝试从 GH_TOKEN 环境变量读取")
    token = os.environ.get("GH_TOKEN", "")

if not token:
    print("错误：无法获取 GitHub token")
    exit(1)

print(f"获取到 token (长度 {len(token)})")

# 创建 Release
installer_path = r"C:\Users\Administrator\Desktop\TDSI服务器官网\downloader\dist\TDSI-Download-Manager-Setup.exe"
file_size = os.path.getsize(installer_path)
file_name = "TDSI-Download-Manager-Setup.exe"

headers = {
    "Authorization": f"token {token}",
    "Accept": "application/vnd.github.v3+json"
}

# 1. 创建 Release
release_data = {
    "tag_name": "v1.0.0",
    "name": "TDM v1.0.0 正式发布",
    "body": """TDSI Download Manager v1.0.0

## 新功能
- 多线程下载管理器
- 断点续传支持
- 智能资源嗅探
- 剪贴板自动监控
- 多主题切换（蓝/绿/紫/粉/暗色）
- Windows 毛玻璃效果
- BT/eD2k 协议支持
- 浏览器扩展自动安装（Chrome/Edge）
- 实时日志窗口
- 单实例运行检测

## 修复
- 导航栏按钮点击问题
- 图标全面改用描边模式确保可见性
- 主题切换动画
- 窗口拖动与缩放

## 安装
下载安装程序后运行，按提示完成安装即可。""",
    "draft": False,
    "prerelease": False
}

print("正在创建 Release...")
resp = requests.post(
    "https://api.github.com/repos/TDSI-g84nm7nfmi/TDM/releases",
    headers=headers, json=release_data
)
print(f"创建 Release: {resp.status_code}")

if resp.status_code in (201, 200):
    release = resp.json()
    upload_url = release["upload_url"].split("{")[0]
    print(f"Release 创建成功: {release['html_url']}")
    
    # 2. 上传安装程序
    print(f"正在上传安装程序 ({file_size/1024/1024:.1f} MB)...")
    with open(installer_path, "rb") as f:
        upload_resp = requests.post(
            upload_url,
            headers={**headers, "Content-Type": "application/octet-stream"},
            params={"name": file_name},
            data=f
        )
    print(f"上传结果: {upload_resp.status_code}")
    if upload_resp.status_code in (201, 200):
        asset = upload_resp.json()
        print(f"安装程序已上传: {asset['browser_download_url']}")
    else:
        print(f"上传失败: {upload_resp.text[:300]}")
else:
    print(f"创建失败: {resp.text[:300]}")
    # 可能已存在，尝试获取已有 release
    resp2 = requests.get(
        "https://api.github.com/repos/TDSI-g84nm7nfmi/TDM/releases/tags/v1.0.0",
        headers=headers
    )
    if resp2.status_code == 200:
        release = resp2.json()
        print(f"Release 已存在: {release['html_url']}")
        # 删除旧 asset 重新上传
        for asset in release.get("assets", []):
            if asset["name"] == file_name:
                print(f"删除旧 asset: {asset['name']}")
                requests.delete(asset["url"], headers=headers)
        
        upload_url = release["upload_url"].split("{")[0]
        with open(installer_path, "rb") as f:
            upload_resp = requests.post(
                upload_url,
                headers={**headers, "Content-Type": "application/octet-stream"},
                params={"name": file_name},
                data=f
            )
        print(f"重新上传: {upload_resp.status_code}")
        if upload_resp.status_code in (201, 200):
            print(f"安装程序已上传: {upload_resp.json()['browser_download_url']}")
