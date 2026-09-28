import json
import subprocess
from pathlib import Path

root = Path(r'E:\Work\dsx')
dll = root / 'src/DsxLite.Cli/bin/Debug/net9.0-windows/DsxLite.Cli.dll'
cases = [
    ['--calibration', '--samples', '2'],
    ['--calibration', '--device', '-1'],
    ['--calibration', '--samples', '0'],
    ['--calibration', '--samples', '2', '--samples', '3'],
    ['--calibration', '--triggers'],
    ['--calibration', '--device'],
    ['--calibration', '--device', 'text'],
]
results = []
for args in cases:
    result = subprocess.run(['dotnet', str(dll), *args], cwd=root, capture_output=True, text=True, encoding='utf-8', timeout=15)
    row = {'args': args, 'exit': result.returncode, 'stdout': result.stdout, 'stderr': result.stderr}
    results.append(row)
    print(json.dumps(row, ensure_ascii=False))
(root / '.tmpfiles/verify-cli-results.json').write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding='utf-8')
