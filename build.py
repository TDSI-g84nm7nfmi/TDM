import os
import sys
import shutil
import subprocess
import platform
import winreg

APP_NAME = "TDSI Download Manager"
APP_SHORT = "TDM"
EXE_NAME = "TDM"


def find_dotnet():
    """查找本机 dotnet SDK"""
    try:
        result = subprocess.run(['where', 'dotnet'], capture_output=True, text=True, shell=True)
        if result.returncode == 0:
            dotnet_path = result.stdout.strip().split('\n')[0]
            if os.path.exists(dotnet_path):
                print(f"在PATH中找到 dotnet: {dotnet_path}")
                return dotnet_path
    except:
        pass
    common = [
        r"C:\\Program Files\\dotnet\\dotnet.exe",
        r"C:\\Program Files (x86)\\dotnet\\dotnet.exe"
    ]
    for p in common:
        if os.path.exists(p):
            print(f"在常见目录找到 dotnet: {p}")
            return p
    print("错误：未找到 dotnet SDK，请先安装 .NET 10 或更高版本！")
    print("下载地址：https://dotnet.microsoft.com/download")
    return None


def find_nsis_makensis():
    """查找 NSIS makensis.exe"""
    try:
        result = subprocess.run(['where', 'makensis'], capture_output=True, text=True, shell=True)
        if result.returncode == 0:
            makensis_path = result.stdout.strip().split('\n')[0]
            if os.path.exists(makensis_path):
                print(f"在PATH中找到makensis: {makensis_path}")
                return makensis_path
    except:
        pass
    reg_paths = [
        r"SOFTWARE\\NSIS",
        r"SOFTWARE\\WOW6432Node\\NSIS",
        r"SOFTWARE\\NSIS64",
        r"SOFTWARE\\WOW6432Node\\NSIS64"
    ]
    for root in [winreg.HKEY_LOCAL_MACHINE, winreg.HKEY_CURRENT_USER]:
        for reg_path in reg_paths:
            try:
                with winreg.OpenKey(root, reg_path) as key:
                    install_dir, _ = winreg.QueryValueEx(key, "InstallDir")
                    makensis_path = os.path.join(install_dir, "Bin", "makensis.exe")
                    if os.path.exists(makensis_path):
                        print(f"在注册表中找到makensis: {makensis_path}")
                        return makensis_path
            except:
                continue
    common_paths = [
        r"C:\\Program Files\\NSIS\\Bin\\makensis.exe",
        r"C:\\Program Files (x86)\\NSIS\\Bin\\makensis.exe",
        r"C:\\NSIS\\Bin\\makensis.exe"
    ]
    for path in common_paths:
        if os.path.exists(path):
            print(f"在常见目录找到makensis: {path}")
            return path
    print("错误：找不到NSIS编译器(makensis)！")
    return None


def build_dotnet():
    """使用 dotnet publish 构建 TDSI Download Manager（非单文件）。
    产物输出到 dist\\publish\\，包含所有运行时文件。
    """
    current_dir = os.path.dirname(os.path.abspath(__file__))
    dotnet = find_dotnet()
    if not dotnet:
        sys.exit(1)
    csproj = os.path.join(current_dir, f'{EXE_NAME}.csproj')
    if not os.path.exists(csproj):
        print(f"错误：未找到 {csproj}")
        sys.exit(1)

    publish_dir = os.path.join(current_dir, 'dist', 'publish')
    if os.path.exists(publish_dir):
        shutil.rmtree(publish_dir, ignore_errors=True)
    os.makedirs(publish_dir, exist_ok=True)

    cmd = [
        dotnet, 'publish', csproj,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishReadyToRun=true',
        '-p:DebugType=embedded',
        f'-o', publish_dir
    ]
    print("执行: " + ' '.join(cmd))
    result = subprocess.run(cmd, cwd=current_dir)
    if result.returncode != 0:
        print("dotnet publish 失败！")
        sys.exit(1)

    # 复制图标到发布目录
    for ico in ['icon.ico', 'setting.ico']:
        src = os.path.join(current_dir, 'image', ico)
        if os.path.exists(src):
            shutil.copy2(src, os.path.join(publish_dir, ico))

    print("构建成功！发布目录:", publish_dir)
    for f in sorted(os.listdir(publish_dir)):
        size = os.path.getsize(os.path.join(publish_dir, f))
        print(f"  {f}  ({size/1024:.0f} KB)")


def create_installer():
    """使用 NSIS 编译安装包。脚本与位图（header.bmp / sidebar.bmp）已静态放在 dist/ 中。"""
    print("\n=== 创建 NSIS 安装程序 ===")
    current_dir = os.path.dirname(os.path.abspath(__file__))
    dist_dir = os.path.join(current_dir, 'dist')
    publish_dir = os.path.join(dist_dir, 'publish')

    # 清理上次的单文件 TDM.exe（如果之前生成过）
    for f in ['TDM.exe']:
        p = os.path.join(dist_dir, f)
        if os.path.exists(p):
            try: os.remove(p)
            except: pass

    if not os.path.exists(publish_dir):
        print(f"错误：未找到发布目录 {publish_dir}，请先运行构建")
        sys.exit(1)

    # 准备图标
    setup_ico = os.path.join(current_dir, 'image', 'setup.ico')
    if not os.path.exists(setup_ico):
        print("错误：image 目录下找不到 setup.ico 文件！")
        sys.exit(1)
    shutil.copy2(setup_ico, os.path.join(dist_dir, 'setup.ico'))

    app_ico = os.path.join(current_dir, 'image', 'icon.ico')
    if os.path.exists(app_ico):
        shutil.copy2(app_ico, os.path.join(dist_dir, 'icon.ico'))

    # 生成 installer UI 位图（header.bmp / sidebar.bmp）
    generate_installer_bitmaps(dist_dir)

    # 写入 LICENSE.txt（NSIS Unicode True 需要 UTF-16LE + BOM）
    license_text = f'''{APP_NAME} (TDM)
Copyright (c) 2024-2026 B站@会飞的附魔下界合金剑
本软件仅供学习和研究使用，请勿用于商业用途。
'''
    license_path = os.path.join(dist_dir, 'LICENSE.txt')
    with open(license_path, 'wb') as f:
        f.write(b'\xff\xfe' + license_text.encode('utf-16-le'))

    # 使用静态 installer.nsi
    script_path = os.path.join(dist_dir, 'installer.nsi')
    if not os.path.exists(script_path):
        print(f"错误：未找到 {script_path}")
        sys.exit(1)

    # 编译
    makensis = find_nsis_makensis()
    if not makensis:
        sys.exit(1)
    print("dist 目录下文件:")
    for f in os.listdir(dist_dir):
        print("  ", f)

    # 临时把 installer.nsi 转为 UTF-16LE + BOM
    utf16_script = script_path + '.utf16.nsi'
    with open(script_path, 'rb') as f:
        data = f.read()
    
    if data.startswith(b'\xff\xfe'):
        import shutil as _sh
        _sh.copy2(script_path, utf16_script)
    elif data.startswith(b'\xef\xbb\xbf'):
        text = data[3:].decode('utf-8')
        with open(utf16_script, 'wb') as f:
            f.write(b'\xff\xfe' + text.encode('utf-16-le'))
    else:
        try:
            text = data.decode('utf-8')
            with open(utf16_script, 'wb') as f:
                f.write(b'\xff\xfe' + text.encode('utf-16-le'))
        except UnicodeDecodeError:
            try:
                text = data.decode('gbk')
                with open(utf16_script, 'wb') as f:
                    f.write(b'\xff\xfe' + text.encode('utf-16-le'))
            except:
                print("警告：无法正确解码 installer.nsi，尝试直接复制")
                import shutil as _sh
                _sh.copy2(script_path, utf16_script)

    result = subprocess.run([makensis, utf16_script], capture_output=True, text=True, cwd=dist_dir)
    print(result.stdout)
    if result.returncode != 0:
        print("NSIS 编译失败！", result.stderr)
        sys.exit(1)

    installer = os.path.join(dist_dir, 'TDSI-Download-Manager-Setup.exe')
    if os.path.exists(installer):
        size_mb = os.path.getsize(installer) / 1024 / 1024
        print("安装包生成成功！")
        print(f"  dist\\TDSI-Download-Manager-Setup.exe ({size_mb:.1f} MB)")

        web_assets_dir = os.path.join(current_dir, 'Web Page', 'assets')
        os.makedirs(web_assets_dir, exist_ok=True)
        web_installer = os.path.join(web_assets_dir, 'TDSI-Download-Manager-Setup.exe')
        shutil.copy2(installer, web_installer)
        print(f"  已复制到 Web Page\\assets\\TDSI-Download-Manager-Setup.exe")
    else:
        print("安装包生成失败！")


def _strip_bom(path):
    """去掉文件的 UTF-8 BOM（NSIS Unicode True 不接受 BOM）。"""
    try:
        with open(path, 'rb') as f:
            data = f.read()
        if data.startswith(b'\xef\xbb\xbf'):
            with open(path, 'wb') as f:
                f.write(data[3:])
    except Exception:
        pass


def generate_installer_bitmaps(dist_dir):
    try:
        from PIL import Image, ImageDraw, ImageFont
    except ImportError:
        print("PIL 未安装，跳过位图生成（使用纯色 fallback）")
        return

    # 找字体
    font_path = None
    for cand in [
        r'C:\Windows\Fonts\msyh.ttc',
        r'C:\Windows\Fonts\msyh.ttf',
        r'C:\Windows\Fonts\msyhbd.ttc',
        r'C:\Windows\Fonts\simhei.ttf',
        r'C:\Windows\Fonts\simsun.ttc',
    ]:
        if os.path.exists(cand):
            font_path = cand
            break

    # header.bmp: 150x57
    w, h = 150, 57
    img = Image.new('RGB', (w, h), (74, 158, 255))
    draw = ImageDraw.Draw(img)
    for y in range(h):
        c = (74 - int(y * 8 / h), 158 - int(y * 10 / h), 255)
        draw.line([(0, y), (w, y)], fill=c)
    if font_path:
        try:
            font = ImageFont.truetype(font_path, 22)
            draw.text((16, 14), 'TDM', fill='white', font=font)
        except Exception:
            pass
    img.save(os.path.join(dist_dir, 'header.bmp'), 'BMP')

    # sidebar.bmp: 164x314
    w, h = 164, 314
    img = Image.new('RGB', (w, h), (240, 245, 252))
    draw = ImageDraw.Draw(img)
    for y in range(0, 90):
        c = (74 - int(y * 20 / 90), 158 - int(y * 10 / 90), 255)
        draw.line([(0, y), (w, y)], fill=c)
    if font_path:
        try:
            font_big = ImageFont.truetype(font_path, 28)
            font_med = ImageFont.truetype(font_path, 13)
            font_small = ImageFont.truetype(font_path, 11)
            draw.text((22, 22), 'TDM', fill='white', font=font_big)
            draw.text((22, 60), 'Download Manager', fill='white', font=font_med)
            feats = ['多线程下载', '断点续传', '智能嗅探', '剪贴板监控', '主题切换', '毛玻璃效果']
            for i, name in enumerate(feats):
                y = 115 + i * 30
                draw.ellipse([(20, y + 1), (30, y + 11)], fill=(74, 158, 255))
                draw.text((38, y), name, fill=(60, 60, 80), font=font_small)
        except Exception:
            pass
    img.save(os.path.join(dist_dir, 'sidebar.bmp'), 'BMP')
    print(f"已生成 installer UI 位图")


def find_signtool():
    """查找 Windows SDK 中的 signtool.exe"""
    try:
        result = subprocess.run(['where', 'signtool'], capture_output=True, text=True, shell=True)
        if result.returncode == 0:
            p = result.stdout.strip().split('\n')[0]
            if os.path.exists(p):
                return p
    except:
        pass
    # 常见路径
    sdk_root = r"C:\\Program Files (x86)\\Windows Kits"
    if os.path.exists(sdk_root):
        for ver in sorted(os.listdir(sdk_root), reverse=True):
            st = os.path.join(sdk_root, ver, "bin", "x64", "signtool.exe")
            if os.path.exists(st):
                return st
    for p in [
        r"C:\\Program Files (x86)\\Windows Kits\\10\\bin\\x64\\signtool.exe",
        r"C:\\Program Files\\Windows Kits\\10\\bin\\x64\\signtool.exe"
    ]:
        if os.path.exists(p):
            return p
    return None


def sign_installer():
    """对安装包进行数字签名（仅在提供了证书时执行）。
    支持环境变量：
      TDM_PFX_FILE  - 证书 .pfx 文件路径
      TDM_PFX_PASS  - 证书密码
      TDM_TIMESTAMP - 时间戳 URL（可选）
    """
    current_dir = os.path.dirname(os.path.abspath(__file__))
    installer = os.path.join(current_dir, 'dist', 'TDSI-Download-Manager-Setup.exe')
    if not os.path.exists(installer):
        print("未找到安装包，跳过签名")
        return

    pfx = os.environ.get('TDM_PFX_FILE', '').strip()
    password = os.environ.get('TDM_PFX_PASS', '').strip()
    ts = os.environ.get('TDM_TIMESTAMP', 'http://timestamp.digicert.com').strip()

    if not pfx or not os.path.exists(pfx):
        print("未提供 TDM_PFX_FILE 环境变量或文件不存在，跳过数字签名")
        print("（可选）设置 TDM_PFX_FILE / TDM_PFX_PASS 后重跑即可对安装包签名")
        return

    signtool = find_signtool()
    if not signtool:
        print("未找到 signtool.exe，跳过签名（请安装 Windows SDK）")
        return

    print(f"正在签名：{installer}")
    cmd = [
        signtool, 'sign',
        '/f', pfx,
        '/p', password,
        '/fd', 'SHA256',
        '/tr', ts,
        '/td', 'SHA256',
        '/d', APP_NAME,
        '/du', 'https://tdsi.top',
        installer
    ]
    result = subprocess.run(cmd)
    if result.returncode != 0:
        print("签名失败！")
        return

    # 验证
    subprocess.run([signtool, 'verify', '/pa', installer])
    print("签名完成！")


def main():
    try:
        print(f"\n=== 构建 {APP_NAME} ({APP_SHORT}) ===")
        build_dotnet()
        print()
        create_installer()
        sign_installer()
        print("\n=== 构建完成！===")
    except Exception as e:
        print(f"\n构建失败：{e}")
        sys.exit(1)


if __name__ == '__main__':
    main()