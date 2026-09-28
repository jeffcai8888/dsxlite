import ctypes
import json
import subprocess
import time
from pathlib import Path
from PIL import ImageGrab

root = Path(r'E:\Work\dsx')
exe = root / '.tmpfiles' / 'verify-app' / 'DsxLite.App.exe'
user32 = ctypes.windll.user32
try:
    user32.SetProcessDPIAware()
except AttributeError:
    pass
process = subprocess.Popen([str(exe)], cwd=str(exe.parent))
windows = []
callback_type = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p)

@callback_type
def collect(hwnd, _):
    pid = ctypes.c_ulong()
    user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    if pid.value == process.pid and user32.IsWindowVisible(hwnd):
        windows.append(hwnd)
    return True

try:
    deadline = time.monotonic() + 20
    while time.monotonic() < deadline and not windows:
        if process.poll() is not None:
            raise RuntimeError(f'应用提前退出: {process.returncode}')
        user32.EnumWindows(collect, 0)
        time.sleep(0.2)
    if not windows:
        raise RuntimeError('未找到新进程的可见窗口')
    hwnd = windows[0]
    user32.SetForegroundWindow(hwnd)
    time.sleep(1)
    rect = (ctypes.c_long * 4)()
    user32.GetWindowRect(hwnd, ctypes.byref(rect))
    bounds = tuple(rect)
    image = root / '.tmpfiles' / 'verify-app-window.png'
    ImageGrab.grab(bbox=bounds).save(image)
    title = ctypes.create_unicode_buffer(512)
    user32.GetWindowTextW(hwnd, title, 512)
    print(json.dumps({'pid': process.pid, 'title': title.value, 'bounds': bounds, 'screenshot': str(image)}, ensure_ascii=False))
finally:
    # 只结束本脚本启动的无设备验证进程，不影响既存应用。
    if process.poll() is None:
        process.terminate()
        process.wait(timeout=10)
