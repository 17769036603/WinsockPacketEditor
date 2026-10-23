"""只读探针：dump 当前战斗的 manager / BattleRole 表结构，用于重新定位 SP（m_Speed）。

临时诊断脚本，只调用现有只读 ADB /proc 读取器：
不写进程内存、不注入、不调用游戏方法、不暂停、不发包、不动游戏文件。
输出：.scratch/sp-probe-<pid>.json 与终端摘要。
"""
from __future__ import annotations

import json
import pathlib
import re
import sys

PANEL = pathlib.Path(r'F:\项目目录\封包监听工具\WinsockPacketEditor\tools\enemy-status-panel')
sys.path.insert(0, str(PANEL))
import battle_reader as b  # noqa: E402

ADB = r'C:\Users\Administrator\AppData\Local\Android\Sdk\platform-tools\adb.exe'
SERIAL = 'emulator-5554'
PKG = 'com.gdoo.dhxy'
OUT_DIR = pathlib.Path(r'F:\项目目录\封包监听工具\WinsockPacketEditor\.scratch')

INTEREST = (
    'm_ActionList', 'm_RoundCmdList', 'm_AllPosInfo', 'm_LearnSkills', 'm_SkillCDDict',
    'm_PetList', 'm_PetSkillList', 'm_PetNeiDanSkillList', 'm_SpareRoleIns', 'm_RoleIns',
    'm_PlayerSkillExp', 'm_BattleStatus', 'm_FightType', 'class',
)
SPEED_RE = re.compile(r'speed|spd|agil|敏|速|order|sort|action|cmd|round|turn', re.I)


def scalar(r, value):
    """Convert a decoded TValue into plain JSON where possible."""
    if not isinstance(value, dict):
        return value
    kind = value.get('kind')
    if kind in ('i32', 'f64', 'boolean', 'nil'):
        return value.get('value')
    if kind != 'tagged':
        return {'kind': kind}
    tag = value.get('tag')
    addr = b.lua.tagged_pointer_from_value(value, b.lua.LJ_TSTR)
    if addr:
        try:
            return b.lua.read_gcstr(r, addr)
        except Exception:
            return {'tag': tag, 'addr': hex(addr), 'error': 'gcstr'}
    for name, expected in (('table', b.lua.LJ_TTAB), ('userdata', 0xFFFFFFF3), ('function', 0xFFFFFFF7)):
        a = b.lua.tagged_pointer_from_value(value, expected)
        if a:
            return {'__' + name + '__': hex(a)}
    return {'tag': tag}


def table_view(r, addr, depth=1, limit=120):
    out = {}
    try:
        nodes = b._nodes(r, addr)
    except Exception as error:  # read failure on a candidate table
        return {'__error__': type(error).__name__}
    for node in nodes[:limit]:
        key = node.get('key')
        value = scalar(r, node.get('value'))
        if depth > 0 and isinstance(value, dict) and '__table__' in value:
            value = table_view(r, int(value['__table__'], 16), depth - 1, limit)
        out[str(key)] = value
    if len(nodes) > limit:
        out['__truncated__'] = len(nodes)
    return out


def main() -> int:
    override = None
    if '--manager' in sys.argv:
        override = int(sys.argv[sys.argv.index('--manager') + 1], 16)
    pid = b._pid(ADB, SERIAL, PKG)
    identity = b._identity(ADB, SERIAL, pid)
    session_key = (SERIAL, PKG, identity['pid'], identity['startTicks'], identity['exe'])
    ranges = b.lua.parse_maps(b._root_adb(ADB, SERIAL, f'cat /proc/{pid}/maps'))
    reader = b.lua.BoundedPageMemoryReader(ADB, SERIAL, pid, ranges, 8)
    try:
        manager = override if override else b._load_manager_cache(session_key)
        source = 'argument' if override else 'cache'
        if manager is None:
            source = 'cold'
            manager, _ = b._cold_discover(reader)
        fields, battle_id, round_number, role_table, position_table = b._manager_values(reader, manager)
        result = {
            'pid': pid, 'identity': identity, 'manager': hex(manager), 'bindingSource': source,
            'battleId': battle_id, 'round': round_number,
            'managerFields': {}, 'managerTables': {}, 'roles': [],
        }
        for name, value in sorted(fields.items()):
            result['managerFields'][name] = scalar(reader, value)
        for name in INTEREST:
            addr = b.lua.tagged_pointer_from_value(fields.get(name, {}), b.lua.LJ_TTAB)
            if addr:
                result['managerTables'][name] = table_view(reader, addr, 2)
        position_by_role = b._position_mapping(reader, position_table)
        userdata_by_position = b._userdata_index(reader, role_table)
        for role_id, position in sorted(position_by_role.items()):
            userdata = userdata_by_position.get(position)
            if userdata is None:
                continue
            try:
                env = b._userdata_env(reader, userdata)
                role_fields = b._field_map(reader, env)
            except Exception as error:
                result['roles'].append({'roleId': role_id, 'position': position,
                                        'error': type(error).__name__})
                continue
            entry = {'roleId': role_id, 'position': position, 'userdata': hex(userdata),
                     'env': hex(env), 'fields': {}, 'tables': {}}
            for name, value in sorted(role_fields.items()):
                entry['fields'][name] = scalar(reader, value)
            for name, value in sorted(role_fields.items()):
                addr = b.lua.tagged_pointer_from_value(value, b.lua.LJ_TTAB)
                if addr and name != 'class':
                    entry['tables'][name] = table_view(reader, addr, 1)
            cls = b.lua.tagged_pointer_from_value(role_fields.get('class', {}), b.lua.LJ_TTAB)
            if cls:
                entry['classMethods'] = sorted(
                    key for key in table_view(reader, cls, 1).keys() if isinstance(key, str)
                )
            result['roles'].append(entry)
        out_path = OUT_DIR / f'sp-probe-{pid}.json'
        out_path.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
        print(json.dumps({
            'pid': pid, 'battleId': battle_id, 'round': round_number,
            'bindingSource': source, 'roles': len(result['roles']),
            'managerFieldCount': len(result['managerFields']),
            'speedLikeKeys': sorted({
                key for key in list(result['managerFields']) + [
                    k for role in result['roles'] for k in role.get('fields', {})
                ] if SPEED_RE.search(str(key))
            }),
            'out': str(out_path),
        }, ensure_ascii=False))
        return 0
    finally:
        reader.close()


if __name__ == '__main__':
    raise SystemExit(main())
