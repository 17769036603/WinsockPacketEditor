"""只读：重试绑定 manager（冷发现易受匿名映射变化影响，故有界重试）。"""
import json
import pathlib
import sys
import time

sys.path.insert(0, r'F:\项目目录\封包监听工具\WinsockPacketEditor\tools\enemy-status-panel')
import battle_reader as b  # noqa: E402

ATTEMPTS = 6
for attempt in range(1, ATTEMPTS + 1):
    try:
        frame = b.sample(serial='emulator-5554', package='com.gdoo.dhxy')
        print(json.dumps({'attempt': attempt, 'state': frame.get('state'),
                          'battleId': frame.get('battleId'), 'round': frame.get('round'),
                          'identity': frame.get('identity')}, ensure_ascii=False), flush=True)
        if frame.get('state') in ('ok', 'not_in_battle'):
            break
    except Exception as error:  # noqa: BLE001 - diagnostic retry loop
        print(json.dumps({'attempt': attempt, 'error': type(error).__name__,
                          'message': str(error)[:200]}, ensure_ascii=False), flush=True)
    time.sleep(3)
